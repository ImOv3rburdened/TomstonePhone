function Set-Lines($path, [System.Collections.Generic.List[string]]$lines)
{
    [System.IO.File]::WriteAllLines($path, $lines)
}

$clientPath = 'E:\Github\TomstonePhone\TomestonePhone\TomestonePhone\Networking\TomestonePhoneClient.cs'
$clientLines = [System.Collections.Generic.List[string]](Get-Content $clientPath)
$logIndex = $clientLines.IndexOf('        this.log = log;')
if ($logIndex -ge 0 -and ($logIndex + 1 -ge $clientLines.Count -or $clientLines[$logIndex + 1] -ne '        this.httpClient.Timeout = TimeSpan.FromSeconds(15);'))
{
    $clientLines.Insert($logIndex + 1, '        this.httpClient.Timeout = TimeSpan.FromSeconds(15);')
}
Set-Lines $clientPath $clientLines

$phonePath = 'E:\Github\TomstonePhone\TomestonePhone\TomestonePhone\UI\PhoneWindow.cs'
$lines = [System.Collections.Generic.List[string]](Get-Content $phonePath)

$handleStart = $lines.IndexOf('    private void HandleAuthFailure(Exception ex)')
$handleEnd = $lines.IndexOf('    private void DrawHomeButton()')
$lines.RemoveRange($handleStart, $handleEnd - $handleStart)
$handleBlock = [string[]]@(
'    private void HandleAuthFailure(Exception ex)',
'    {',
'        var message = ex.ToString();',
'        if (message.Contains("Invalid username or password", StringComparison.OrdinalIgnoreCase) && this.autoLoginAttempted)',
'        {',
'            this.configuration.ClearRememberedCredentials();',
'            this.SaveConfiguration();',
'        }',
'',
'        if (message.Contains("403") || message.Contains("banned", StringComparison.OrdinalIgnoreCase) || message.Contains("forbidden", StringComparison.OrdinalIgnoreCase))',
'        {',
'            this.configuration.LocalAccountLockout = true;',
'            this.configuration.LocalAccountLockoutReason = "This device is locked due to a banned account or IP restriction.";',
'            this.configuration.AuthToken = null;',
'            this.configuration.Username = null;',
'            this.configuration.ClearRememberedCredentials();',
'            this.SaveConfiguration();',
'            this.pendingStatus = "Device locked";',
'            return;',
'        }',
'',
'        this.pendingStatus = string.IsNullOrWhiteSpace(ex.Message) ? "Authentication failed" : ex.Message;',
'    }',
''
)
$lines.InsertRange($handleStart, $handleBlock)

$ensureStart = $lines.IndexOf('    private void EnsureSessionHydrated()')
$ensureEnd = $lines.IndexOf('    private void ProcessBackgroundTasks()')
$lines.RemoveRange($ensureStart, $ensureEnd - $ensureStart)
$ensureBlock = [string[]]@(
'    private void EnsureSessionHydrated()',
'    {',
'        if (string.IsNullOrWhiteSpace(this.configuration.AuthToken))',
'        {',
'            this.refreshOnNextDraw = false;',
'            return;',
'        }',
'',
'        var missingProfile = this.state.CurrentProfile.AccountId == Guid.Empty',
'            || string.IsNullOrWhiteSpace(this.state.CurrentProfile.PhoneNumber)',
'            || string.Equals(this.state.CurrentProfile.Username, "Guest", StringComparison.OrdinalIgnoreCase);',
'',
'        if (!this.refreshOnNextDraw && !missingProfile)',
'        {',
'            return;',
'        }',
'',
'        this.QueueSnapshotRefresh();',
'    }',
''
)
$lines.InsertRange($ensureStart, $ensureBlock)

$processStart = $lines.IndexOf('    private void ProcessBackgroundTasks()')
$processEnd = $lines.IndexOf('    private bool HasHydratedAuthenticatedProfile()')
$lines.RemoveRange($processStart, $processEnd - $processStart)
$processBlock = [string[]]@(
'    private void ProcessBackgroundTasks()',
'    {',
'        if (this.pendingAuthTask is { IsCompleted: true })',
'        {',
'            var result = this.pendingAuthTask.GetAwaiter().GetResult();',
'            this.pendingAuthTask = null;',
'            if (result.Error is not null)',
'            {',
'                this.HandleAuthFailure(result.Error);',
'            }',
'            else if (!string.IsNullOrWhiteSpace(result.Username) && !string.IsNullOrWhiteSpace(result.AuthToken))',
'            {',
'                this.configuration.Username = result.Username;',
'                this.configuration.AuthToken = result.AuthToken;',
'                this.configuration.StoreRememberedCredentials(this.loginUsername, this.loginPassword);',
'                this.pendingStatus = result.StatusMessage ?? "Signed in";',
'                this.SaveConfiguration();',
'                this.showHomeScreen = true;',
'                this.autoLoginAttempted = false;',
'                this.refreshOnNextDraw = true;',
'                this.QueueSnapshotRefresh();',
'            }',
'        }',
'',
'        if (this.pendingSnapshotTask is { IsCompleted: true })',
'        {',
'            var result = this.pendingSnapshotTask.GetAwaiter().GetResult();',
'            this.pendingSnapshotTask = null;',
'            if (result.Error is not null)',
'            {',
'                if (this.IsUnauthorizedError(result.Error))',
'                {',
'                    this.configuration.AuthToken = null;',
'                    this.SaveConfiguration();',
'                    if (this.TryBeginAutoLogin("Session expired. Restoring..."))',
'                    {',
'                        return;',
'                    }',
'                }',
'',
'                this.refreshOnNextDraw = false;',
'                this.pendingStatus = "Sync failed";',
'                this.service.Log.Warning(result.Error, "Failed to refresh TomestonePhone snapshot.");',
'            }',
'            else if (result.Snapshot is not null)',
'            {',
'                this.state.ApplySnapshot(result.Snapshot);',
'                if (result.UpdatedProfile is not null)',
'                {',
'                    this.state.CurrentProfile = result.UpdatedProfile;',
'                }',
'',
'                if (this.state.CurrentProfile.Status == AccountStatus.Banned)',
'                {',
'                    this.configuration.LocalAccountLockout = true;',
'                    this.configuration.LocalAccountLockoutReason = "This device is locked because the linked account was banned.";',
'                    this.configuration.AuthToken = null;',
'                    this.configuration.Username = null;',
'                    this.configuration.ClearRememberedCredentials();',
'                    this.SaveConfiguration();',
'                    this.pendingStatus = "Device locked";',
'                }',
'                else',
'                {',
'                    this.refreshOnNextDraw = false;',
'                    this.pendingStatus = $"Synced {DateTime.Now:t}";',
'                }',
'            }',
'        }',
'    }',
''
)
$lines.InsertRange($processStart, $processBlock)

Set-Lines $phonePath $lines
