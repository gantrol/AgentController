#!/usr/bin/env python3
"""Loopback dsh-stream-v1 adapter for Qwen3-ASR's native vLLM streaming API."""

from __future__ import annotations

import argparse
import asyncio
import json
import logging
from dataclasses import dataclass
from typing import Any

import numpy as np
from aiohttp import WSMsgType, web


PROTOCOL = "dsh-stream-v1"
SAMPLE_RATE = 16_000
MAX_CONTROL_BYTES = 64 * 1024
MAX_AUDIO_BYTES = 256 * 1024

LANGUAGES = {
    "ar": "Arabic",
    "cs": "Czech",
    "da": "Danish",
    "de": "German",
    "el": "Greek",
    "en": "English",
    "es": "Spanish",
    "fa": "Persian",
    "fi": "Finnish",
    "fil": "Filipino",
    "fr": "French",
    "hi": "Hindi",
    "hu": "Hungarian",
    "id": "Indonesian",
    "it": "Italian",
    "ja": "Japanese",
    "ko": "Korean",
    "ms": "Malay",
    "nl": "Dutch",
    "pl": "Polish",
    "pt": "Portuguese",
    "ro": "Romanian",
    "ru": "Russian",
    "sv": "Swedish",
    "th": "Thai",
    "tr": "Turkish",
    "vi": "Vietnamese",
    "yue": "Cantonese",
    "zh": "Chinese",
}


class ProtocolError(Exception):
    pass


def parse_control(text: str) -> dict[str, Any]:
    if len(text.encode("utf-8")) > MAX_CONTROL_BYTES:
        raise ProtocolError("Control frame is too large.")
    try:
        value = json.loads(text)
    except json.JSONDecodeError as error:
        raise ProtocolError("Control frame is invalid JSON.") from error
    if not isinstance(value, dict):
        raise ProtocolError("Control frame must be an object.")
    return value


def qwen_language(value: Any) -> str | None:
    if not isinstance(value, str) or not value.strip():
        return None
    normalized = value.strip().lower().replace("_", "-")
    if normalized in {"auto", "und"}:
        return None
    return LANGUAGES.get(normalized, LANGUAGES.get(normalized.split("-", 1)[0]))


def pcm_float(raw: bytes) -> np.ndarray:
    if not raw or len(raw) > MAX_AUDIO_BYTES or len(raw) % 2 != 0:
        raise ProtocolError("PCM frame must contain bounded 16-bit samples.")
    return np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0


async def send(ws: web.WebSocketResponse, frame_type: str, **fields: Any) -> None:
    await ws.send_str(json.dumps({"type": frame_type, **fields}, ensure_ascii=False))


@dataclass(frozen=True)
class ServerConfig:
    model: str
    chunk_seconds: float
    unfixed_chunks: int
    unfixed_tokens: int
    voice_threshold: float
    pre_roll_seconds: float


class StreamingServer:
    def __init__(self, model: Any, config: ServerConfig) -> None:
        self.model = model
        self.config = config
        self.decode_lock = asyncio.Lock()

    async def health(self, _request: web.Request) -> web.Response:
        return web.json_response({
            "ready": True,
            "protocol": PROTOCOL,
            "model": self.config.model,
        })

    async def stream(self, request: web.Request) -> web.StreamResponse:
        ws = web.WebSocketResponse(
            autoping=True,
            heartbeat=20,
            max_msg_size=MAX_AUDIO_BYTES,
            compress=False,
        )
        await ws.prepare(request)
        if self.decode_lock.locked():
            await send(ws, "error", message="Local Qwen ASR is already serving another stream.")
            await ws.close(code=1013, message=b"busy")
            return ws

        async with self.decode_lock:
            try:
                await self._recognize(ws)
            except ProtocolError as error:
                await send(ws, "error", message=str(error))
                await ws.close(code=1008, message=b"protocol error")
            except asyncio.CancelledError:
                raise
            except Exception:
                logging.exception("Streaming recognition failed")
                await send(ws, "error", message="Local Qwen ASR recognition failed; inspect the service log.")
                await ws.close(code=1011, message=b"recognition failed")
        return ws

    async def _recognize(self, ws: web.WebSocketResponse) -> None:
        first = await asyncio.wait_for(ws.receive(), timeout=15)
        if first.type != WSMsgType.TEXT:
            raise ProtocolError("The first frame must be start JSON.")
        start = parse_control(first.data)
        if start.get("type") != "start" or start.get("protocol") != PROTOCOL:
            raise ProtocolError("Unsupported streaming protocol.")
        if start.get("encoding") != "pcm_s16le":
            raise ProtocolError("Only pcm_s16le audio is supported.")
        if start.get("sampleRate") != SAMPLE_RATE or start.get("channels") != 1:
            raise ProtocolError("Audio must be mono 16 kHz PCM16.")

        state = await asyncio.to_thread(
            self.model.init_streaming_state,
            language=qwen_language(start.get("language")),
            unfixed_chunk_num=self.config.unfixed_chunks,
            unfixed_token_num=self.config.unfixed_tokens,
            chunk_size_sec=self.config.chunk_seconds,
        )
        previous = ""
        voice_started = False
        pre_roll: list[np.ndarray] = []
        pre_roll_samples = 0
        max_pre_roll_samples = int(round(self.config.pre_roll_seconds * SAMPLE_RATE))
        await send(ws, "ready")

        async for message in ws:
            if message.type == WSMsgType.BINARY:
                samples = pcm_float(message.data)
                if not voice_started:
                    rms = float(np.sqrt(np.mean(np.square(samples), dtype=np.float64)))
                    if rms < self.config.voice_threshold:
                        pre_roll.append(samples)
                        pre_roll_samples += int(samples.size)
                        while pre_roll_samples > max_pre_roll_samples and pre_roll:
                            pre_roll_samples -= int(pre_roll.pop(0).size)
                        continue
                    voice_started = True
                    if pre_roll:
                        samples = np.concatenate([*pre_roll, samples])
                        pre_roll.clear()
                await asyncio.to_thread(self.model.streaming_transcribe, samples, state)
                current = str(state.text or "")
                if current != previous:
                    previous = current
                    await send(ws, "partial", text=current)
                continue
            if message.type == WSMsgType.TEXT:
                control = parse_control(message.data)
                if control.get("type") == "cancel":
                    return
                if control.get("type") != "stop":
                    raise ProtocolError("Only stop or cancel is valid after start.")
                if not voice_started:
                    await send(ws, "final", text="")
                    await send(ws, "done")
                    await ws.close(code=1000, message=b"complete")
                    return
                await asyncio.to_thread(self.model.finish_streaming_transcribe, state)
                final_text = str(state.text or "")
                await send(ws, "final", text=final_text)
                await send(ws, "done")
                await ws.close(code=1000, message=b"complete")
                return
            if message.type in {WSMsgType.CLOSE, WSMsgType.CLOSED, WSMsgType.ERROR}:
                return


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Qwen3-ASR dsh-stream-v1 adapter")
    parser.add_argument("--model", default="Qwen/Qwen3-ASR-0.6B")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--gpu-memory-utilization", type=float, default=0.55)
    parser.add_argument("--max-new-tokens", type=int, default=64)
    parser.add_argument("--max-model-len", type=int, default=8192)
    parser.add_argument("--max-num-seqs", type=int, default=1)
    parser.add_argument("--chunk-seconds", type=float, default=2.0)
    parser.add_argument("--unfixed-chunks", type=int, default=2)
    parser.add_argument("--unfixed-tokens", type=int, default=5)
    parser.add_argument("--voice-threshold", type=float, default=0.008)
    parser.add_argument("--pre-roll-seconds", type=float, default=0.3)
    parsed = parser.parse_args()
    if not 1 <= parsed.port <= 65_535:
        parser.error("--port must be from 1 to 65535")
    if not 0.05 <= parsed.gpu_memory_utilization <= 0.95:
        parser.error("--gpu-memory-utilization must be from 0.05 to 0.95")
    if not 0.25 <= parsed.chunk_seconds <= 10:
        parser.error("--chunk-seconds must be from 0.25 to 10")
    if not 0 <= parsed.voice_threshold <= 0.25:
        parser.error("--voice-threshold must be from 0 to 0.25")
    if not 0 <= parsed.pre_roll_seconds <= 2:
        parser.error("--pre-roll-seconds must be from 0 to 2")
    return parsed


def main() -> None:
    args = arguments()
    logging.basicConfig(level=logging.INFO, format="[dsh-qwen-asr] %(levelname)s %(message)s")
    logging.info("Loading %s with the vLLM streaming backend…", args.model)
    try:
        from qwen_asr import Qwen3ASRModel
    except ImportError as error:
        raise SystemExit(
            "qwen-asr with the vLLM extra is not installed in this Python environment."
        ) from error
    model = Qwen3ASRModel.LLM(
        model=args.model,
        gpu_memory_utilization=args.gpu_memory_utilization,
        max_new_tokens=args.max_new_tokens,
        max_model_len=args.max_model_len,
        max_num_seqs=args.max_num_seqs,
    )
    logging.info("Model loaded; serving %s://%s:%d/v1/stream", "ws", args.host, args.port)
    server = StreamingServer(model, ServerConfig(
        model=args.model,
        chunk_seconds=args.chunk_seconds,
        unfixed_chunks=args.unfixed_chunks,
        unfixed_tokens=args.unfixed_tokens,
        voice_threshold=args.voice_threshold,
        pre_roll_seconds=args.pre_roll_seconds,
    ))
    app = web.Application(client_max_size=MAX_AUDIO_BYTES)
    app.router.add_get("/health", server.health)
    app.router.add_get("/v1/stream", server.stream)
    web.run_app(app, host=args.host, port=args.port, print=None)


if __name__ == "__main__":
    main()
