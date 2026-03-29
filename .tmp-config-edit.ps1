$p = 'E:\Github\TomstonePhone\TomestonePhone\TomestonePhone\Configuration.cs'
$lines = [System.Collections.Generic.List[string]](Get-Content $p)
$usingIndex = $lines.IndexOf('using System.Security.Cryptography;')
if ($usingIndex -ge 0) { $lines.RemoveAt($usingIndex) }
$protectStart = $lines.IndexOf('    private static string? ProtectString(string value)')
$unprotectStart = $lines.IndexOf('    private static string? UnprotectString(string? value)')
$lines.RemoveRange($protectStart, $unprotectStart - $protectStart)
$protectBlock = [string[]]@(
'    private static string? ProtectString(string value)',
'    {',
'        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));',
'    }',
''
)
$lines.InsertRange($protectStart, $protectBlock)
$unprotectStart = $lines.IndexOf('    private static string? UnprotectString(string? value)')
$catchIndex = $lines.IndexOf('        catch')
$endIndex = $catchIndex + 5
$lines.RemoveRange($unprotectStart, $endIndex - $unprotectStart)
$unprotectBlock = [string[]]@(
'    private static string? UnprotectString(string? value)',
'    {',
'        if (string.IsNullOrWhiteSpace(value))',
'        {',
'            return null;',
'        }',
'',
'        try',
'        {',
'            return Encoding.UTF8.GetString(Convert.FromBase64String(value));',
'        }',
'        catch',
'        {',
'            return null;',
'        }',
'    }'
)
$lines.InsertRange($unprotectStart, $unprotectBlock)
[System.IO.File]::WriteAllLines($p, $lines)
