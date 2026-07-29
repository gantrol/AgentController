#pragma once

#define WIN32_NO_STATUS
#include <windows.h>
#undef WIN32_NO_STATUS
#include <wdf.h>
#include <hidport.h>

typedef UCHAR HID_REPORT_DESCRIPTOR, *PHID_REPORT_DESCRIPTOR;

#define CODEX_MICRO_VENDOR_ID                 0x303A
#define CODEX_MICRO_PRODUCT_ID                0x8360
#define CODEX_MICRO_VERSION                   0x0100

#define CODEX_MICRO_REPORT_ID                 0x06
#define CODEX_MICRO_INJECT_FEATURE_ID         0xF0
#define CODEX_MICRO_OUTPUT_FEATURE_ID         0xF1
#define CODEX_MICRO_INFO_FEATURE_ID           0xF2

#define CODEX_MICRO_WIRE_REPORT_LENGTH        64U
#define CODEX_MICRO_FEATURE_REPORT_LENGTH     66U
#define CODEX_MICRO_QUEUE_CAPACITY            128U

#define CODEX_MICRO_MANUFACTURER_STRING       L"OpenAI"
#define CODEX_MICRO_PRODUCT_STRING            L"Codex Micro Simulator"
#define CODEX_MICRO_SERIAL_STRING             L"CODEX-MICRO-UMDF-0001"
#define CODEX_MICRO_INDEXED_STRING            L"Codex Micro UMDF2 virtual HID"
#define CODEX_MICRO_INDEXED_STRING_ID         5U

typedef struct _DEVICE_CONTEXT {
    WDFDEVICE Device;
    WDFQUEUE DefaultQueue;
    WDFQUEUE ManualReadQueue;
    HID_DEVICE_ATTRIBUTES Attributes;
    HID_DESCRIPTOR HidDescriptor;
    PHID_REPORT_DESCRIPTOR ReportDescriptor;

    LARGE_INTEGER ConnectionEpoch;
    ULONG DroppedInputReports;
    ULONG DroppedOutputReports;

    ULONG InputHead;
    ULONG InputTail;
    ULONG InputCount;
    UCHAR InputReports[CODEX_MICRO_QUEUE_CAPACITY][CODEX_MICRO_WIRE_REPORT_LENGTH];

    ULONG OutputHead;
    ULONG OutputTail;
    ULONG OutputCount;
    UCHAR OutputReports[CODEX_MICRO_QUEUE_CAPACITY][CODEX_MICRO_WIRE_REPORT_LENGTH];
} DEVICE_CONTEXT, *PDEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, DeviceGetContext)

typedef struct _QUEUE_CONTEXT {
    PDEVICE_CONTEXT DeviceContext;
} QUEUE_CONTEXT, *PQUEUE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(QUEUE_CONTEXT, QueueGetContext)

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD CodexMicroEvtDeviceAdd;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL CodexMicroEvtIoDeviceControl;

NTSTATUS
RequestCopyFromBuffer(
    _In_ WDFREQUEST Request,
    _In_reads_bytes_(Length) const VOID* Source,
    _In_ size_t Length
    );

NTSTATUS
RequestGetHidXferPacketToRead(
    _In_ WDFREQUEST Request,
    _Out_ HID_XFER_PACKET* Packet
    );

NTSTATUS
RequestGetHidXferPacketToWrite(
    _In_ WDFREQUEST Request,
    _Out_ HID_XFER_PACKET* Packet
    );
