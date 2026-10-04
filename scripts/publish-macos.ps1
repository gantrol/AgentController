[CmdletBinding()]
param(
    [ValidateSet('osx-arm64', 'osx-x64')]
    [string[]] $Runtime = @('osx-arm64', 'osx-x64'),

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $OutputRoot
)

throw 'AgentController macOS Foundation Preview is retired. See docs/architecture/platform-direction.zh-CN.md for the current Codex Micro macOS direction.'
