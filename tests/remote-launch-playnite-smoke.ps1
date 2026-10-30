param([Parameter(Mandatory = $true)][string]$FixturePath)
$ErrorActionPreference = 'Stop'
$fixture = Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json
try {
    if (-not ([uri]$fixture.base_url).IsLoopback) { throw 'Smoke tests require localhost.' }
    if (-not $PlayniteApi) { throw 'Run inside the isolated Playnite profile.' }
    $plugin = @($PlayniteApi.Addons.Plugins | Where-Object { $_.Id -eq [guid]'4a8d9c7b-2e54-4a6b-9c6d-0a2b8f1e7d43' })
    if ($plugin.Count -ne 1) { throw 'The built extension was not loaded exactly once.' }
    $plugin = $plugin[0]
    $game = $PlayniteApi.Database.Games.Get([guid]$fixture.local_id)
    if (-not $game) {
        $game = New-Object Playnite.SDK.Models.Game
        $game.Id = [guid]$fixture.local_id
        $game.Name = 'Remote launch verification'
        $game.IsInstalled = $true
        $game.GameActions = New-Object 'System.Collections.ObjectModel.ObservableCollection[Playnite.SDK.Models.GameAction]'
        $action = New-Object Playnite.SDK.Models.GameAction
        $action.Type = [Playnite.SDK.Models.GameActionType]::File
        $action.Path = $fixture.marker_exe
        $action.IsPlayAction = $true
        $game.GameActions.Add($action)
        $PlayniteApi.Database.Games.Add($game)
    }
    $settings = $plugin.Settings
    $settings.ApiUrl = $fixture.base_url
    $protection = $plugin.GetType().Assembly.GetType('UnnamedTrackingPlaynite.CredentialProtection')
    $protect = $protection.GetMethod('Protect', [Reflection.BindingFlags]'Static,NonPublic')
    $settings.ProtectedCredential = $protect.Invoke($null, @([string]$fixture.credential))
    $settings.SignedInServer = $fixture.base_url
    $settings.ConnectedAs = 'Disposable launch verification'
    $settings.GrantedScopes = 'profile.read library.sync games.launch'
    $settings.RemoteLaunchEnabled = $true
    $settings.ConfirmRemoteLaunch = $false
    $save = $plugin.GetType().GetMethod('SavePluginSettings').MakeGenericMethod([type[]]@($settings.GetType()))
    $null = $save.Invoke($plugin, @($settings))
    $restart = $plugin.GetType().GetMethod('RefreshRemoteLaunchAsync', [Reflection.BindingFlags]'Instance,NonPublic')
    $null = $restart.Invoke($plugin, @())
    [IO.File]::WriteAllText($fixture.ready_path, 'Isolated Playnite receiver configured.')
} catch {
    [IO.File]::WriteAllText($fixture.failure_path, $_.Exception.Message)
    throw
}
