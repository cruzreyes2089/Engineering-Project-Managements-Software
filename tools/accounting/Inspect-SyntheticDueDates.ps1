# Read-only diagnostic for a NEW, isolated, manually populated synthetic business.
# Windows PowerShell 5.1 or PowerShell 7. No database or financial writes.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Net.Http
$businessName = 'CRC DUE DATE TEST'
$markerName = 'CRC Due Date Test Identity'
$expected = [ordered]@{
    'CRC-DUE-IMMEDIATE' = '2026-11-01'
    'CRC-DUE-NET30' = '2026-12-01'
    'CRC-DUE-FIXED' = '2026-12-15'
}
function Need([bool]$condition,[string]$reason) { if (-not $condition) { throw $reason } }
function Has($obj,[string]$name) { return $null -ne $obj -and $null -ne $obj.PSObject.Properties[$name] }
function Get-Field($obj,[string]$name) {
    if (-not (Has $obj $name)) { throw 'Required response field missing.' }
    return $obj.PSObject.Properties[$name].Value
}
function Read-Guid($value) {
    $parsed = [guid]::Empty
    Need ([guid]::TryParseExact([string]$value,'D',[ref]$parsed) -and $parsed -ne [guid]::Empty) 'Invalid response identifier.'
    return $parsed
}
function Identity($root) {
    Need ((Get-Field (Get-Field $root 'business') 'name') -ceq $businessName) 'Wrong business. No report created.'
}
function Read-Json([string]$route) {
    # Strict fixed allowlist, no redirects, proxy, cookies, arbitrary URL or write method.
    Need ($route -match '^/(?:text-custom-fields\?term=CRC%20Due%20Date%20Test%20Identity&skip=0&pageSize=2|sales-invoices\?skip=0&pageSize=20|sales-invoice-form/[0-9a-fA-F-]{36})$') 'Route not permitted.'
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,($endpoint+$route))
    $response = $null; $stream = $null; $memory = $null
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(15))
    try {
        $response = $http.SendAsync($request,[Net.Http.HttpCompletionOption]::ResponseHeadersRead,$deadline.Token).GetAwaiter().GetResult()
        Need ([int]$response.StatusCode -eq 200) 'GET did not return HTTP 200.'
        Need ($null -ne $response.Content.Headers.ContentType -and $response.Content.Headers.ContentType.MediaType -eq 'application/json') 'Unexpected response content type.'
        $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $memory = [IO.MemoryStream]::new()
        $buffer = New-Object byte[] 16384
        while ($true) {
            $n = $stream.ReadAsync($buffer,0,$buffer.Length,$deadline.Token).GetAwaiter().GetResult()
            if ($n -eq 0) { break }
            Need (($memory.Length+$n) -le 1MB) 'Response exceeds diagnostic bound.'
            $memory.Write($buffer,0,$n)
        }
        $raw = [Text.UTF8Encoding]::new($false,$true).GetString($memory.ToArray())
        $root = ConvertFrom-Json -InputObject $raw -ErrorAction Stop
        Need ($root -is [pscustomobject]) 'Unexpected response root.'
        return $root
    } finally {
        if ($null -ne $memory) { $memory.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
        if ($null -ne $response) { $response.Dispose() }
        $request.Dispose(); $deadline.Dispose()
    }
}
function Read-Marker {
    $r = Read-Json '/text-custom-fields?term=CRC%20Due%20Date%20Test%20Identity&skip=0&pageSize=2'
    Identity $r
    $fields = @(Get-Field $r 'textCustomFields')
    Need ((Get-Field $r 'skip') -eq 0 -and (Get-Field $r 'totalRecords') -eq 1 -and $fields.Count -eq 1 -and (Get-Field $fields[0] 'name') -ceq $markerName) 'Exact synthetic marker required.'
    return (Read-Guid (Get-Field $fields[0] 'key'))
}
function Due-Fields($obj) {
    $result = [ordered]@{}
    foreach ($p in @($obj.PSObject.Properties | Sort-Object Name)) {
        if ($p.Name -notmatch 'due|overdue|issuedate|^days$|^netdays$') { continue }
        Need ($p.Name -match '^[A-Za-z0-9_]{1,80}$') 'Unexpected diagnostic field name.'
        $v = $p.Value
        # Only primitive values; nested data is not shared.
        if ($null -eq $v) { $result[$p.Name] = $null }
        elseif ($v -is [datetime]) { $result[$p.Name] = $v.ToString('yyyy-MM-ddTHH:mm:ss',[Globalization.CultureInfo]::InvariantCulture) }
        elseif ($v -is [bool] -or $v -is [int] -or $v -is [long] -or $v -is [decimal] -or $v -is [double]) { $result[$p.Name] = $v }
        elseif ($v -is [string] -and $v -notmatch '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}' -and $v -match '^[A-Za-z0-9 _:+.\-]{0,80}$') { $result[$p.Name] = $v }
        else { $result[$p.Name] = '[nonprimitive omitted]' }
    }
    return $result
}
function Sweep {
    $r = Read-Json '/sales-invoices?skip=0&pageSize=20'
    Identity $r
    $rows = @(Get-Field $r 'salesInvoices')
    Need ((Get-Field $r 'skip') -eq 0 -and (Get-Field $r 'totalRecords') -eq 3 -and (Get-Field $r 'pageSize') -ge 3 -and $rows.Count -eq 3) 'Exactly three synthetic invoices required.'
    $seen = [Collections.Generic.HashSet[guid]]::new()
    $referenceKeys = [Collections.Generic.List[string]]::new()
    $customers = [Collections.Generic.HashSet[guid]]::new()
    $evidence = [Collections.Generic.List[object]]::new()
    foreach ($ref in $expected.Keys) {
        $matches = @($rows | Where-Object { (Get-Field $_ 'reference') -ieq $ref })
        Need ($matches.Count -eq 1) 'Missing or duplicate test reference.'
        $row = $matches[0]
        Need ((Get-Field $row 'reference') -ceq $ref) 'Reference case differs.'
        $key = Read-Guid (Get-Field $row 'key')
        Need ($seen.Add($key)) 'Duplicate invoice identifier.'
        $referenceKeys.Add($ref+':'+$key.ToString('D'))
        $amount = Get-Field $row 'invoiceAmount'; $balance = Get-Field $row 'balanceDue'
        Need ($null -eq (Get-Field $amount 'currency') -and $null -eq (Get-Field $balance 'currency') -and (Get-Field $amount 'value') -eq 100 -and (Get-Field $balance 'value') -eq 100) 'Unexpected synthetic invoice amount, currency or balance.'
        $detail = Read-Json ('/sales-invoice-form/'+$key.ToString('D'))
        Need ((Read-Guid (Get-Field $detail 'Key')) -eq $key -and (Read-Guid (Get-Field $detail 'id')) -eq $key -and (Get-Field $detail 'Reference') -ceq $ref) 'Detail identity mismatch.'
        $null = $customers.Add((Read-Guid (Get-Field $detail 'Customer')))
        $issue = Get-Field $detail 'IssueDate'
        if ($issue -is [datetime]) { $issue = $issue.ToString('yyyy-MM-ddTHH:mm:ss',[Globalization.CultureInfo]::InvariantCulture) }
        Need ($issue -ceq '2026-11-01T00:00:00') 'Synthetic issue date differs.'
        $evidence.Add([ordered]@{ reference=$ref; expectedDueDate=$expected[$ref]; registerFields=(Due-Fields $row); formFields=(Due-Fields $detail) })
    }
    Need ($customers.Count -eq 1) 'Expected one synthetic customer.'
    # Compare normalized selected fields and exact reference-to-key associations, not full JSON.
    return [pscustomobject]@{ report=$evidence.ToArray(); internalKeys=($referenceKeys.ToArray() -join ','); internalCustomer=(@($customers) -join ',') }
}
$http = $null; $handler = $null; $secret = $null; $bstr = [IntPtr]::Zero
try {
    Need ((Read-Host 'Type CRC DUE DATE TEST to confirm the NEW synthetic business is open') -ceq $businessName) 'Synthetic scope not confirmed.'
    Need ((Read-Host 'Type USD to confirm this test business uses USD only') -ceq 'USD') 'Currency scope not confirmed.'
    $port = 0
    Need ([int]::TryParse((Read-Host 'Manager API2 loopback port'),[ref]$port) -and $port -ge 1 -and $port -le 65535) 'Invalid port.'
    $endpoint = 'http://127.0.0.1:'+$port+'/api2'
    $secret = Read-Host 'API token for the synthetic business (not displayed)' -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    $clear = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    Need ($clear -match '^[!-~]{1,4096}$') 'Invalid token shape.'
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false; $handler.UseProxy = $false; $handler.UseCookies = $false
    $http = [Net.Http.HttpClient]::new($handler,$false)
    $http.DefaultRequestHeaders.Add('X-API-KEY',$clear)
    $http.DefaultRequestHeaders.Accept.ParseAdd('application/json')
    $clear = $null
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr); $bstr = [IntPtr]::Zero
    $marker = Read-Marker
    $first = Sweep; $second = Sweep
    Need ((Read-Marker) -eq $marker) 'Marker changed during review.'
    Need ((ConvertTo-Json $first -Depth 10 -Compress) -ceq (ConvertTo-Json $second -Depth 10 -Compress)) 'Observed invoice identity or due-date fields changed.'
    Write-Output 'CRC DUE DATE QUALIFICATION — READ ONLY'
    Write-Output (ConvertTo-Json -InputObject $second.report -Depth 8)
    Write-Output 'Three synthetic invoice records observed twice; no POST, PUT, DELETE, receipt or payment created.'
    Write-Output 'Expected dates are operator setup targets, not yet verified API interpretations. Compare with Manager UI before approval.'
} catch {
    # Never emit request bodies, raw responses, URLs, keys or tokens from exception details.
    Write-Output 'DIAGNOSTIC STOPPED. No successful qualification assumed. No records written. Verify the exact synthetic setup, local connection and API token.'
} finally {
    if ($null -ne $http) { $http.Dispose() }
    if ($null -ne $handler) { $handler.Dispose() }
    if ($bstr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    if ($null -ne $secret) { $secret.Dispose() }
    $clear = $null
}
