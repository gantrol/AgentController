use std::{collections::HashMap, env, fs, net::IpAddr, path::PathBuf, time::Duration};

use anyhow::{Context as _, Result, bail};
use serde::{Deserialize, Serialize};
use url::Url;

const PROTOCOL_VERSION: u8 = 1;
const PROTOCOL_SOURCE: &str = "codex-micro";
const CONTROL_PATH: &str = "/__agentcontroller/micro/request";
const MAX_RESPONSE_BYTES: u64 = 64 * 1024;
const MAX_SESSIONS: usize = 6;

pub const DEFAULT_CONTROL_URI: &str = "http://127.0.0.1:3080/__agentcontroller/micro/request";
pub const STATE_POLL_INTERVAL: Duration = Duration::from_secs(2);

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BridgeEndpoint(Url);

impl BridgeEndpoint {
    pub fn discover() -> Result<Self> {
        if let Some(value) = command_line_endpoint()? {
            return Self::parse(&value).context("invalid --endpoint value");
        }

        if let Some(value) = env::var_os("DEEPSEEK_KEYPAD_CONTROL_URI") {
            let value = value
                .into_string()
                .map_err(|_| anyhow::anyhow!("DEEPSEEK_KEYPAD_CONTROL_URI is not UTF-8"))?;
            return Self::parse(&value).context("invalid DEEPSEEK_KEYPAD_CONTROL_URI value");
        }

        if let Some(value) = endpoint_from_shared_settings()? {
            return Self::parse(&value).context("invalid DeepSeek endpoint in shared settings");
        }

        Self::parse(DEFAULT_CONTROL_URI)
    }

    pub fn parse(value: &str) -> Result<Self> {
        let url = Url::parse(value).context("control URI is not an absolute URL")?;
        if url.scheme() != "http" {
            bail!("control URI must use plain HTTP on loopback");
        }
        if !url.username().is_empty() || url.password().is_some() {
            bail!("control URI must not contain credentials");
        }
        if url.query().is_some() || url.fragment().is_some() {
            bail!("control URI must not contain a query or fragment");
        }
        if url.path() != CONTROL_PATH {
            bail!("control URI path must be {CONTROL_PATH}");
        }

        let host = url
            .host_str()
            .context("control URI must contain a loopback host")?;
        let is_loopback = host.eq_ignore_ascii_case("localhost")
            || host
                .parse::<IpAddr>()
                .is_ok_and(|address| address.is_loopback());
        if !is_loopback {
            bail!("control URI host must be loopback");
        }

        Ok(Self(url))
    }

    pub fn control_uri(&self) -> &str {
        self.0.as_str()
    }

    pub fn surface_uri(&self) -> Url {
        let mut url = self.0.clone();
        url.set_path("/");
        url.set_query(Some("codexMicroSurface=1"));
        url
    }
}

#[derive(Clone)]
pub struct BridgeClient {
    endpoint: BridgeEndpoint,
    agent: ureq::Agent,
}

impl BridgeClient {
    pub fn new(endpoint: BridgeEndpoint) -> Self {
        let configuration = ureq::Agent::config_builder()
            .proxy(None)
            .timeout_connect(Some(Duration::from_millis(650)))
            .timeout_global(Some(Duration::from_secs(5)))
            .build();
        Self {
            endpoint,
            agent: configuration.into(),
        }
    }

    pub fn endpoint(&self) -> &BridgeEndpoint {
        &self.endpoint
    }

    pub fn activate(&self) -> Result<CommandResponse> {
        self.command(BridgeRequest::activate())
    }

    pub fn activate_session(&self, session_id: &str) -> Result<CommandResponse> {
        self.command(BridgeRequest::activate_session(session_id))
    }

    pub fn execute_action(&self, action_id: ActionId) -> Result<CommandResponse> {
        self.command(BridgeRequest::execute(action_id, None))
    }

    pub fn read_state(&self) -> Result<StateSnapshot> {
        let response = self.exchange(&BridgeRequest::state())?;
        if !response.success {
            bail!(response.message);
        }
        let mut state = response
            .state
            .context("DeepSeek Bridge returned no state snapshot")?;
        state.sessions.truncate(MAX_SESSIONS);
        Ok(state)
    }

    fn command(&self, request: BridgeRequest<'_>) -> Result<CommandResponse> {
        let response = self.exchange(&request)?;
        Ok(CommandResponse {
            success: response.success,
            message: response.message,
            status: response.status.unwrap_or_default(),
        })
    }

    fn exchange(&self, request: &BridgeRequest<'_>) -> Result<BridgeResponse> {
        let mut response = self
            .agent
            .post(self.endpoint.control_uri())
            .send_json(request)
            .context("DeepSeek Bridge request failed")?;
        response
            .body_mut()
            .with_config()
            .limit(MAX_RESPONSE_BYTES)
            .read_json()
            .context("DeepSeek Bridge returned malformed JSON")
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ActionId {
    NewSession,
    ToggleConversationView,
    CancelTurn,
    ForkSession,
    ToggleSidebar,
    OpenDetails,
    ComposerSelectPrevious,
    ComposerSelectNext,
    ComposerActivateSelection,
    ToggleQuickModel,
}

impl ActionId {
    pub const fn as_str(self) -> &'static str {
        match self {
            Self::NewSession => "session/new",
            Self::ToggleConversationView => "view/toggle-chat-trajectory",
            Self::CancelTurn => "turn/cancel",
            Self::ForkSession => "session/fork",
            Self::ToggleSidebar => "layout/toggle-sidebar",
            Self::OpenDetails => "layout/open-details",
            Self::ComposerSelectPrevious => "composer/select-previous",
            Self::ComposerSelectNext => "composer/select-next",
            Self::ComposerActivateSelection => "composer/activate-selection",
            Self::ToggleQuickModel => "model/toggle-quick",
        }
    }
}

#[derive(Clone, Debug)]
pub struct CommandResponse {
    pub success: bool,
    pub message: String,
    pub status: ResponseStatus,
}

#[derive(Clone, Copy, Debug, Default, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum ResponseStatus {
    Completed,
    Opening,
    Foreground,
    Background,
    #[default]
    #[serde(other)]
    Unknown,
}

#[derive(Clone, Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StateSnapshot {
    #[serde(default)]
    pub capabilities: Capabilities,
    #[serde(default)]
    pub sessions: Vec<SessionSummary>,
    pub current_session_id: Option<String>,
    pub components: Option<ComponentSnapshot>,
}

impl StateSnapshot {
    pub fn browser_connected(&self) -> bool {
        self.components
            .as_ref()
            .is_some_and(|components| components.browser == "connected")
    }
}

#[derive(Clone, Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Capabilities {
    #[serde(default)]
    pub session_activation: bool,
    #[serde(default)]
    pub actions: Vec<String>,
}

impl Capabilities {
    pub fn supports(&self, action_id: ActionId) -> bool {
        self.actions.iter().any(|value| value == action_id.as_str())
    }
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SessionSummary {
    pub id: String,
    #[serde(default)]
    pub status: SessionStatus,
}

#[derive(Clone, Copy, Debug, Default, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum SessionStatus {
    Running,
    Completed,
    Waiting,
    Error,
    #[default]
    #[serde(other)]
    Idle,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ComponentSnapshot {
    pub browser: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct BridgeResponse {
    success: bool,
    #[serde(default)]
    message: String,
    status: Option<ResponseStatus>,
    state: Option<StateSnapshot>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct BridgeRequest<'a> {
    version: u8,
    source: &'static str,
    action: &'static str,
    #[serde(skip_serializing_if = "Option::is_none")]
    action_id: Option<&'static str>,
    #[serde(skip_serializing_if = "Option::is_none")]
    session_id: Option<&'a str>,
}

impl<'a> BridgeRequest<'a> {
    fn activate() -> Self {
        Self::new("activate")
    }

    fn state() -> Self {
        Self::new("state/read")
    }

    fn activate_session(session_id: &'a str) -> Self {
        Self {
            session_id: Some(session_id),
            ..Self::new("session/activate")
        }
    }

    fn execute(action_id: ActionId, session_id: Option<&'a str>) -> Self {
        Self {
            action_id: Some(action_id.as_str()),
            session_id,
            ..Self::new("action/execute")
        }
    }

    fn new(action: &'static str) -> Self {
        Self {
            version: PROTOCOL_VERSION,
            source: PROTOCOL_SOURCE,
            action,
            action_id: None,
            session_id: None,
        }
    }
}

#[derive(Default, Deserialize)]
struct StoredSettings {
    #[serde(default, rename = "Harnesses", alias = "harnesses")]
    harnesses: HashMap<String, StoredConnection>,
}

#[derive(Deserialize)]
struct StoredConnection {
    #[serde(rename = "ControlUri", alias = "controlUri")]
    control_uri: Option<String>,
}

fn command_line_endpoint() -> Result<Option<String>> {
    let mut arguments = env::args_os().skip(1);
    let mut endpoint = None;
    while let Some(argument) = arguments.next() {
        if argument != "--endpoint" {
            bail!("unsupported argument: {}", argument.to_string_lossy());
        }
        let value = arguments.next().context("--endpoint requires a value")?;
        if endpoint.is_some() {
            bail!("--endpoint may only be supplied once");
        }
        endpoint = Some(
            value
                .into_string()
                .map_err(|_| anyhow::anyhow!("--endpoint value is not UTF-8"))?,
        );
    }
    Ok(endpoint)
}

fn endpoint_from_shared_settings() -> Result<Option<String>> {
    let Some(local_app_data) = env::var_os("LOCALAPPDATA") else {
        return Ok(None);
    };
    let path = PathBuf::from(local_app_data)
        .join("CodexMicro")
        .join("harness-settings.json");
    if !path.is_file() {
        return Ok(None);
    }

    let contents =
        fs::read_to_string(&path).with_context(|| format!("failed to read {}", path.display()))?;
    let settings: StoredSettings = serde_json::from_str(&contents)
        .with_context(|| format!("failed to parse {}", path.display()))?;
    Ok(settings
        .harnesses
        .iter()
        .find(|(id, _)| id.eq_ignore_ascii_case("deepseek-harness"))
        .and_then(|(_, connection)| connection.control_uri.clone())
        .filter(|value| !value.trim().is_empty()))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn endpoint_is_restricted_to_exact_loopback_control_path() {
        assert!(BridgeEndpoint::parse(DEFAULT_CONTROL_URI).is_ok());
        assert!(
            BridgeEndpoint::parse("http://localhost:3090/__agentcontroller/micro/request").is_ok()
        );
        assert!(
            BridgeEndpoint::parse("https://127.0.0.1:3080/__agentcontroller/micro/request")
                .is_err()
        );
        assert!(
            BridgeEndpoint::parse("http://example.com/__agentcontroller/micro/request").is_err()
        );
        assert!(BridgeEndpoint::parse("http://127.0.0.1:3080/").is_err());
    }

    #[test]
    fn surface_uri_is_derived_from_the_validated_endpoint() {
        let endpoint =
            BridgeEndpoint::parse("http://127.0.0.1:3097/__agentcontroller/micro/request")
                .expect("test endpoint should be valid");
        assert_eq!(
            endpoint.surface_uri().as_str(),
            "http://127.0.0.1:3097/?codexMicroSurface=1"
        );
    }

    #[test]
    fn action_request_uses_the_versioned_bridge_contract() {
        let request = BridgeRequest::execute(ActionId::ForkSession, Some("session-1"));
        let value = serde_json::to_value(request).expect("request should serialize");
        assert_eq!(value["version"], 1);
        assert_eq!(value["source"], "codex-micro");
        assert_eq!(value["action"], "action/execute");
        assert_eq!(value["actionId"], "session/fork");
        assert_eq!(value["sessionId"], "session-1");
    }

    #[test]
    fn state_deserialization_preserves_authoritative_session_status() {
        let response: BridgeResponse = serde_json::from_value(serde_json::json!({
            "success": true,
            "message": "ok",
            "state": {
                "capabilities": {
                    "sessionActivation": true,
                    "actions": ["session/new"]
                },
                "sessions": [{"id": "s1", "status": "waiting"}],
                "currentSessionId": "s1",
                "components": {"adapter": "ready", "browser": "connected"}
            }
        }))
        .expect("response should deserialize");
        let state = response.state.expect("state should be present");
        assert_eq!(state.sessions[0].status, SessionStatus::Waiting);
        assert!(state.browser_connected());
        assert!(state.capabilities.supports(ActionId::NewSession));
    }
}
