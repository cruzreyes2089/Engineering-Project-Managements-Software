# Read-only diagnostic for a NEW, isolated, manually populated synthetic business.
# Revision 3: bounded currency and amount diagnostics with exact synthetic values. Windows PowerShell 5.1 or PowerShell 7.
[CmdletBinding()]
param([switch]$SelfTest)
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
$script:stage = 'Setup'
$script:failure = 'Unexpected local or API error'
$script:httpStatus = $null
function Stage([string]$label) {
    $script:stage = $label
    $script:failure = 'Unexpected local or API error'
    $script:httpStatus = $null
}
function Need([bool]$condition,[string]$reason) {
    if (-not $condition) {
        $script:failure = $reason
        throw 'Diagnostic validation stopped.'
    }
}
function Has($obj,[string]$name) { return $null -ne $obj -and $null -ne $obj.PSObject.Properties[$name] }
function Get-Field($obj,[string]$name) {
    Need (Has $obj $name) ('Required response field missing: '+$name)
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
    if ($SelfTest) { return (Read-TestResponse $route) }
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,($endpoint+$route))
    $response = $null; $stream = $null; $memory = $null
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(15))
    try {
        $response = $http.SendAsync($request,[Net.Http.HttpCompletionOption]::ResponseHeadersRead,$deadline.Token).GetAwaiter().GetResult()
        $script:httpStatus = [int]$response.StatusCode
        Need ($script:httpStatus -eq 200) 'GET did not return HTTP 200.'
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
    Stage 'Read synthetic business marker'
    $r = Read-Json '/text-custom-fields?term=CRC%20Due%20Date%20Test%20Identity&skip=0&pageSize=2'
    Identity $r
    Write-Host 'PASS: expected business identity'
    Stage 'Validate synthetic text custom field'
    $fields = @(Get-Field $r 'textCustomFields')
    Need ((Get-Field $r 'skip') -eq 0 -and (Get-Field $r 'totalRecords') -eq 1 -and $fields.Count -eq 1 -and (Get-Field $fields[0] 'name') -ceq $markerName) 'Exact synthetic marker required.'
    $markerKey = Read-Guid (Get-Field $fields[0] 'key')
    Write-Host 'PASS: exact synthetic marker'
    return $markerKey
}
# No raw money objects or currency UUIDs are printed. Distinguish null currency,
# explicit USD, and an unknown encoding. Unknown encodings remain unqualified.
function Money-Value($obj,[string]$label) {
    $currency = Get-Field $obj 'currency'
    $value = Get-Field $obj 'value'
    $currencyKind = 'Unsupported'
    if ($null -eq $currency) { $currencyKind = 'Null' }
    elseif ($currency -is [string] -and $currency -ceq 'USD') { $currencyKind = 'USD' }
    elseif ($currency -is [string] -and $currency -eq '') { $currencyKind = 'Empty string' }
    elseif ($currency -is [string]) {
        $parsed = [guid]::Empty
        if ([guid]::TryParse($currency,[ref]$parsed)) { $currencyKind = 'UUID' }
        else { $currencyKind = 'Other string' }
    }
    elseif ($currency -is [pscustomobject]) { $currencyKind = 'Object' }
    $number = $value -is [int] -or $value -is [long] -or $value -is [decimal] -or $value -is [double]
    $matches = $number -and $value -eq 100
    Write-Host ($label+' currency encoding: '+$currencyKind)
    Write-Host ($label+' numeric USD 100 match: '+$matches)
    return [pscustomobject]@{ CurrencyKind=$currencyKind; Matches=$matches }
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
    Stage 'Read synthetic invoice register'
    $r = Read-Json '/sales-invoices?skip=0&pageSize=20'
    Identity $r
    $rows = @(Get-Field $r 'salesInvoices')
    Need ((Get-Field $r 'skip') -eq 0 -and (Get-Field $r 'totalRecords') -eq 3 -and (Get-Field $r 'pageSize') -ge 3 -and $rows.Count -eq 3) 'Exactly three synthetic invoices required.'
    Write-Host 'PASS: exactly three synthetic invoice rows'
    $seen = [Collections.Generic.HashSet[guid]]::new()
    $referenceKeys = [Collections.Generic.List[string]]::new()
    $customers = [Collections.Generic.HashSet[guid]]::new()
    $evidence = [Collections.Generic.List[object]]::new()
    foreach ($ref in $expected.Keys) {
        Stage ('Validate register row: '+$ref)
        $matches = @($rows | Where-Object { (Get-Field $_ 'reference') -ieq $ref })
        Need ($matches.Count -eq 1) 'Missing or duplicate test reference.'
        $row = $matches[0]
        Need ((Get-Field $row 'reference') -ceq $ref) 'Reference case differs.'
        $key = Read-Guid (Get-Field $row 'key')
        Need ($seen.Add($key)) 'Duplicate invoice identifier.'
        $referenceKeys.Add($ref+':'+$key.ToString('D'))
        $amount = Get-Field $row 'invoiceAmount'; $balance = Get-Field $row 'balanceDue'
        $amountCheck = Money-Value $amount 'Invoice amount'
        $balanceCheck = Money-Value $balance 'Outstanding balance'
        Need ($amountCheck.Matches) 'Invoice amount does not equal numeric 100.'
        Need ($balanceCheck.Matches) 'Outstanding balance does not equal numeric 100.'
        Need ($amountCheck.CurrencyKind -in @('Null','USD') -and $balanceCheck.CurrencyKind -in @('Null','USD')) 'Currency encoding is not qualified; review reported encoding categories.'
        Need ($amountCheck.CurrencyKind -eq $balanceCheck.CurrencyKind) 'Invoice and outstanding currency encoding differs.'
        Stage ('Read invoice detail: '+$ref)
        $detail = Read-Json ('/sales-invoice-form/'+$key.ToString('D'))
        Stage ('Validate invoice detail: '+$ref)
        Need ((Read-Guid (Get-Field $detail 'Key')) -eq $key -and (Read-Guid (Get-Field $detail 'id')) -eq $key -and (Get-Field $detail 'Reference') -ceq $ref) 'Detail identity mismatch.'
        $null = $customers.Add((Read-Guid (Get-Field $detail 'Customer')))
        $issue = Get-Field $detail 'IssueDate'
        if ($issue -is [datetime]) { $issue = $issue.ToString('yyyy-MM-ddTHH:mm:ss',[Globalization.CultureInfo]::InvariantCulture) }
        Need ($issue -ceq '2026-11-01T00:00:00') 'Synthetic issue date differs.'
        Write-Host ('PASS: invoice identity, date and amount — '+$ref)
        $evidence.Add([ordered]@{ reference=$ref; expectedDueDate=$expected[$ref]; currencyEncoding=$amountCheck.CurrencyKind; registerFields=(Due-Fields $row); formFields=(Due-Fields $detail) })
    }
    Stage 'Validate common synthetic customer'
    Need ($customers.Count -eq 1) 'Expected one synthetic customer.'
    # Compare normalized selected fields and exact reference-to-key associations, not full JSON.
    return [pscustomobject]@{ report=$evidence.ToArray(); internalKeys=($referenceKeys.ToArray() -join ','); internalCustomer=(@($customers) -join ',') }
}
# Offline fixtures use invented due-field names. They qualify diagnostic mechanics,
# never installed Manager field semantics. No endpoint, token or network client is used.
function Read-TestResponse([string]$route) {
    if ($route.StartsWith('/text-custom-fields?')) {
        return [pscustomobject]@{ business=[pscustomobject]@{name=$script:testBusiness}; skip=0; totalRecords=1; textCustomFields=@([pscustomobject]@{name=$markerName;key=$script:testMarker}) }
    }
    if ($route.StartsWith('/sales-invoices?')) {
        return [pscustomobject]@{ business=[pscustomobject]@{name=$script:testBusiness}; skip=0; pageSize=20; totalRecords=$script:testRows.Count; salesInvoices=$script:testRows }
    }
    if ($route.StartsWith('/sales-invoice-form/')) {
        return $script:testDetails[$route.Substring('/sales-invoice-form/'.Length)]
    }
    throw 'Unexpected test route.'
}
function Run-SelfTest {
    $script:testBusiness=$businessName
    $script:testMarker='10000000-0000-0000-0000-000000000001'
    $customer='20000000-0000-0000-0000-000000000001'
    $script:testRows=@()
    $script:testDetails=@{}
    $i=0
    foreach ($reference in $expected.Keys) {
        $i++
        $key='30000000-0000-0000-0000-00000000000'+$i
        $money=[pscustomobject]@{currency=$null;value=100}
        $script:testRows+= [pscustomobject]@{key=$key;reference=$reference;invoiceAmount=$money;balanceDue=$money;dueDate=$expected[$reference]}
        $script:testDetails[$key]=[pscustomobject]@{Key=$key;id=$key;Reference=$reference;IssueDate='2026-11-01T00:00:00';Customer=$customer;DueMode=$i;DueDate=$expected[$reference];DuePrivate=$customer;Unrelated='not for output'}
    }
    function Expect-Block([scriptblock]$action,[string]$label) {
        $blocked=$false
        try { $null = & $action } catch { $blocked=$true }
        Need $blocked ('Expected rejection: '+$label)
        Write-Host ('PASS: '+$label)
    }
    $marker=Read-Marker
    Need ($marker.ToString('D') -eq $script:testMarker) 'Marker self-test failed.'
    $first=Sweep
    $second=Sweep
    Need ((ConvertTo-Json $first -Depth 10 -Compress) -ceq (ConvertTo-Json $second -Depth 10 -Compress)) 'Stable fixture self-test failed.'
    $report=ConvertTo-Json -InputObject $first.report -Depth 8 -Compress
    Need ($first.report.Count -eq 3 -and $report.Contains('2026-12-15') -and -not $report.Contains($customer) -and -not $report.Contains('Unrelated') -and -not $report.Contains('30000000-')) 'Diagnostic redaction self-test failed.'
    Write-Host 'PASS: stable three-invoice diagnostic and redaction'
    $script:testBusiness='Other business'
    Expect-Block { Read-Marker } 'wrong marker business rejected'
    Expect-Block { Sweep } 'wrong invoice business rejected'
    $script:testBusiness=$businessName
    $saved=$script:testRows[1].reference
    $script:testRows[1].reference=$script:testRows[0].reference
    Expect-Block { Sweep } 'duplicate reference rejected'
    $script:testRows[1].reference=$saved
    $usd=Money-Value ([pscustomobject]@{currency='USD';value=100}) 'Self-test'
    Need ($usd.CurrencyKind -eq 'USD' -and $usd.Matches) 'Explicit USD recognition failed.'
    $unknown=Money-Value ([pscustomobject]@{currency=$customer;value=100}) 'Self-test'
    Need ($unknown.CurrencyKind -eq 'UUID' -and $unknown.Matches) 'Currency UUID classification failed.'
    $savedCurrency=$script:testRows[0].invoiceAmount
    $script:testRows[0].invoiceAmount=[pscustomobject]@{currency=$customer;value=100}
    Expect-Block { Sweep } 'unqualified currency encoding rejected'
    $script:testRows[0].invoiceAmount=$savedCurrency
    $saved=$script:testRows[0].balanceDue
    $script:testRows[0].balanceDue=[pscustomobject]@{currency=$null;value=99}
    Expect-Block { Sweep } 'unexpected balance rejected'
    $script:testRows[0].balanceDue=$saved
    $key=$script:testRows[0].key
    $saved=$script:testDetails[$key].Customer
    $script:testDetails[$key].Customer='20000000-0000-0000-0000-000000000002'
    Expect-Block { Sweep } 'mixed customer identities rejected'
    $script:testDetails[$key].Customer=$saved
    $script:testDetails[$key].DueMode=99
    $changed=Sweep
    Need ((ConvertTo-Json $first -Depth 10 -Compress) -cne (ConvertTo-Json $changed -Depth 10 -Compress)) 'Changed due field not detected.'
    Write-Host 'PASS: changed due field distinguished'
    Expect-Block { Read-Json '/payments' } 'nonallowlisted route rejected'
    Write-Host 'Due-date diagnostic offline tests passed. No actual Manager API access.'
}
if ($SelfTest) { Run-SelfTest; return }
$http = $null; $handler = $null; $secret = $null; $bstr = [IntPtr]::Zero
try {
    Write-Host 'CRC DUE DATE DIAGNOSTIC — REVISION 3'
    Stage 'Confirm synthetic scope'
    Need ((Read-Host 'Type CRC DUE DATE TEST to confirm the NEW synthetic business is open') -ceq $businessName) 'Synthetic scope not confirmed.'
    Need ((Read-Host 'Type USD to confirm this test business uses USD only') -ceq 'USD') 'Currency scope not confirmed.'
    $port = 0
    Need ([int]::TryParse((Read-Host 'Manager API2 loopback port'),[ref]$port) -and $port -ge 1 -and $port -le 65535) 'Invalid port.'
    $endpoint = 'http://127.0.0.1:'+$port+'/api2'
    Stage 'Read API credential'
    $secret = Read-Host 'API token for the synthetic business (not displayed)' -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    $clear = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    Need ($clear -match '^[!-~]{1,4096}$') 'Invalid token shape.'
    Stage 'Initialize local HTTP connection'
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false; $handler.UseProxy = $false; $handler.UseCookies = $false
    $http = [Net.Http.HttpClient]::new($handler,$false)
    $http.DefaultRequestHeaders.Add('X-API-KEY',$clear)
    $http.DefaultRequestHeaders.Accept.ParseAdd('application/json')
    $clear = $null
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr); $bstr = [IntPtr]::Zero
    $marker = Read-Marker
    $first = Sweep; $second = Sweep
    $lastMarker = Read-Marker
    Stage 'Compare marker and repeated invoice observations'
    Need ($lastMarker -eq $marker) 'Marker changed during review.'
    Need ((ConvertTo-Json $first -Depth 10 -Compress) -ceq (ConvertTo-Json $second -Depth 10 -Compress)) 'Observed invoice identity or due-date fields changed.'
    Write-Output 'CRC DUE DATE QUALIFICATION — READ ONLY'
    Write-Output (ConvertTo-Json -InputObject $second.report -Depth 8)
    Write-Output 'Three synthetic invoice records observed twice; no POST, PUT, DELETE, receipt or payment created.'
    Write-Output 'Expected dates are operator setup targets, not yet verified API interpretations. Compare with Manager UI before approval.'
} catch {
    # Never emit request bodies, raw responses, URLs, keys or tokens from exception details.
    Write-Output 'DIAGNOSTIC STOPPED — REVISION 3'
    Write-Output ('Stage: '+$script:stage)
    Write-Output ('Reason: '+$script:failure)
    if ($null -ne $script:httpStatus) { Write-Output ('HTTP status: '+$script:httpStatus) }
    Write-Output ('Exception type: '+$_.Exception.GetType().Name)
    Write-Output 'No successful qualification assumed. No records written. Do not share your token.'
} finally {
    if ($null -ne $http) { $http.Dispose() }
    if ($null -ne $handler) { $handler.Dispose() }
    if ($bstr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    if ($null -ne $secret) { $secret.Dispose() }
    $clear = $null
}
