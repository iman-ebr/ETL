<#
.SYNOPSIS
    Creates a disposable demo database (LocalDB by default), applies the EF migrations and fills PERSONEL_Sender
    with ~4,200 SYNTHETIC personnel rows (fictional names, checksum-valid generated national codes).

    Deliberate defects are included so the dashboard has something to show:
      * every 350th row: national code with a wrong check digit     -> ValidationFailed
      * every 900th row: malformed e-mail                           -> ValidationFailed
      * every 700th row: mobile typed with Persian digits           -> normalized, then sent
      * every 500th row: empty Latin first/last name                -> sent
      * one duplicated PER_ID                                       -> quarantined, never sent
      * one national code shared by two PerIds                      -> second one rejected with 409

.EXAMPLE
    ./tools/Seed-DemoDatabase.ps1
    ./tools/Seed-DemoDatabase.ps1 -Server "(localdb)\MSSQLLocalDB" -Database MapnaEtlDemo -Rows 10000
#>
param(
    [string] $Server = '(localdb)\MSSQLLocalDB',
    [string] $Database = 'MapnaEtlDemo',
    [int] $Rows = 4200
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$master = "Server=$Server;Integrated Security=True;TrustServerCertificate=True"
$cs = "Server=$Server;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True"

function Invoke-Sql([string] $conn, [string] $sql) {
    $c = New-Object System.Data.SqlClient.SqlConnection $conn; $c.Open()
    try { $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 120; $cmd.ExecuteScalar() } finally { $c.Close() }
}

Write-Host "Recreating $Database on $Server ..."
Invoke-Sql $master "IF DB_ID('$Database') IS NOT NULL BEGIN ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database]; END; CREATE DATABASE [$Database];" | Out-Null

Write-Host "Applying EF migrations ..."
dotnet ef database update --project "$root/Mapna.LogData" --startup-project "$root/Mapna.LogData" --connection $cs
if ($LASTEXITCODE -ne 0) { throw "dotnet ef database update failed (install with: dotnet tool install -g dotnet-ef)" }

$first  = 'علی','محمد','حسین','رضا','مهدی','امیر','سارا','مریم','زهرا','فاطمه','نرگس','الهام','حمید','سعید','پریسا','نیلوفر','کاوه','بهاره','آرش','مینا','یاسمن','پویا','شیما','فرهاد','لیلا','بابک','سمیرا','کیان','نگار','هادی'
$last   = 'احمدی','محمدی','حسینی','رضایی','کریمی','موسوی','جعفری','صادقی','رحیمی','نوری','اکبری','کاظمی','قاسمی','مرادی','حیدری','شریفی','زمانی','یزدانی','طاهری','عباسی','فراهانی','نجفی','سلیمانی','بهرامی','توکلی'
$latinF = 'Ali','Mohammad','Hossein','Reza','Mahdi','Amir','Sara','Maryam','Zahra','Fatemeh','Narges','Elham','Hamid','Saeed','Parisa','Niloofar','Kaveh','Bahareh','Arash','Mina','Yasaman','Pouya','Shima','Farhad','Leila','Babak','Samira','Kian','Negar','Hadi'
$latinL = 'Ahmadi','Mohammadi','Hosseini','Rezaei','Karimi','Mousavi','Jafari','Sadeghi','Rahimi','Nouri','Akbari','Kazemi','Ghasemi','Moradi','Heidari','Sharifi','Zamani','Yazdani','Taheri','Abbasi','Farahani','Najafi','Soleimani','Bahrami','Tavakoli'

function New-NationalCode([long] $seed) {
    [long] $v = [long]100000000 + (($seed * [long]7919) % [long]800000000)
    $body = $v.ToString('D9'); $sum = 0
    for ($i = 0; $i -lt 9; $i++) { $sum += ([int]::Parse([string]$body[$i])) * (10 - $i) }
    $r = $sum % 11
    return $body + $(if ($r -lt 2) { $r } else { 11 - $r })
}

$t = New-Object System.Data.DataTable
foreach ($col in 'PER_ID','PER_NAME','PER_SURNAME','PER_STATUS','SEX_CODE','PER_EMAIL','MOBIL_NO','PHONE','PER_ADDR','PER_LNAME','PER_LSURNAME','BORN_DATE','NATIONAL_CODE','USER_PRINCIPAL_NAME','PER_CONTRACT','COMPANY_ID') { [void]$t.Columns.Add($col) }
$rnd = New-Object System.Random 1404
for ($i = 1; $i -le $Rows; $i++) {
    $fi = $rnd.Next($first.Count); $li = $rnd.Next($last.Count); $id = 100000 + $i
    $mobile = '09' + $rnd.Next(10, 40) + $rnd.Next(1000000, 9999999)
    $email = "$($latinF[$fi].ToLower()).$($latinL[$li].ToLower())$i@mapna.example"
    $nc = New-NationalCode $id; $ln = $latinF[$fi]; $ls = $latinL[$li]
    if ($i % 350 -eq 0) { $nc = $nc.Substring(0, 9) + (([int]::Parse([string]$nc[9]) + 1) % 10) }
    if ($i % 900 -eq 0) { $email = "broken-email-$i" }
    if ($i % 700 -eq 0) { $mobile = -join ($mobile.ToCharArray() | ForEach-Object { [char](0x06F0 + [int]::Parse([string]$_)) }) }
    if ($i % 500 -eq 0) { $ln = ''; $ls = '' }
    $row = $t.NewRow()
    $row.PER_ID = $id; $row.PER_NAME = $first[$fi]; $row.PER_SURNAME = $last[$li]; $row.PER_STATUS = 1
    $row.SEX_CODE = $(if ($fi -ge 6 -and $fi -le 11) { 'F' } else { 'M' }); $row.PER_EMAIL = $email; $row.MOBIL_NO = $mobile
    $row.PHONE = '021' + $rnd.Next(10000000, 99999999); $row.PER_ADDR = 'تهران'; $row.PER_LNAME = $ln; $row.PER_LSURNAME = $ls
    $row.BORN_DATE = '13' + $rnd.Next(50, 80) + '/0' + $rnd.Next(1, 9) + '/1' + $rnd.Next(0, 9); $row.NATIONAL_CODE = $nc
    $row.USER_PRINCIPAL_NAME = "u$id@mapna.example"; $row.PER_CONTRACT = 'رسمی'; $row.COMPANY_ID = '10'
    $t.Rows.Add($row)
}
$dup = $t.NewRow(); $dup.ItemArray = $t.Rows[10].ItemArray; $dup.PER_NAME = 'رکورد'; $dup.PER_SURNAME = 'تکراری'; $dup.NATIONAL_CODE = (New-NationalCode 999001); $t.Rows.Add($dup)
$clash = $t.NewRow(); $clash.ItemArray = $t.Rows[20].ItemArray; $clash.PER_ID = 999999; $clash.PER_NAME = 'کد ملی'; $clash.PER_SURNAME = 'مشترک'; $t.Rows.Add($clash)

Invoke-Sql $cs @"
CREATE TABLE dbo.PERSONEL_Sender (
    PER_ID INT NOT NULL, PER_NAME NVARCHAR(100), PER_SURNAME NVARCHAR(100), PER_STATUS INT NOT NULL, SEX_CODE NVARCHAR(10),
    PER_EMAIL NVARCHAR(256), MOBIL_NO NVARCHAR(30), PHONE NVARCHAR(30), PER_ADDR NVARCHAR(500), PER_LNAME NVARCHAR(100),
    PER_LSURNAME NVARCHAR(100), BORN_DATE NVARCHAR(20), NATIONAL_CODE NVARCHAR(30), USER_PRINCIPAL_NAME NVARCHAR(256),
    PER_CONTRACT NVARCHAR(50), COMPANY_ID NVARCHAR(50));
"@ | Out-Null

$conn = New-Object System.Data.SqlClient.SqlConnection $cs; $conn.Open()
$bulk = New-Object System.Data.SqlClient.SqlBulkCopy $conn; $bulk.DestinationTableName = 'dbo.PERSONEL_Sender'
foreach ($col in $t.Columns) { [void]$bulk.ColumnMappings.Add($col.ColumnName, $col.ColumnName) }
$bulk.WriteToServer($t); $conn.Close()

Write-Host "Seeded $($t.Rows.Count) rows into $Database.PERSONEL_Sender."
Write-Host "Connection string for both apps: $cs"
