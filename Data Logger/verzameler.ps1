<#
.SYNOPSIS
    Verzamelt de inhoud van gespecificeerde bestandstypen uit een map en submappen
    naar één outputbestand, waarbij bepaalde mappen worden overgeslagen.

.DESCRIPTION
    Dit script doorzoekt recursief een bronmap ($sourceFolder). Het neemt alleen bestanden op
    die een extensie hebben uit de $allowedExtensions lijst OF een bestandsnaam hebben uit de
    $allowedFilenames lijst. Bestanden in paden die een van de $excludeDirPatterns bevatten
    (zoals '\bin\' of '\obj\') worden overgeslagen. De volledige paden en de inhoud
    van de opgenomen bestanden worden samengevoegd in het $outputFile bestand.

.NOTES
    Author: Gemini AI (gebaseerd op eerdere batch script)
    Date: 2025-04-29
    Voor het uitvoeren van dit script moet mogelijk het Execution Policy worden aangepast.
    Bijv. `Set-ExecutionPolicy RemoteSigned -Scope CurrentUser` in een PowerShell venster.
#>

#Requires -Version 5.1

#----------------------------------------------------
#region Configuratie
#----------------------------------------------------

# Stel hier de volledige map in waar de code staat die je wilt verzamelen.
$sourceFolder = "C:\Users\marten-desktop\Documents\Data-Logger" # Voorbeeld: "C:\Users\JouwNaam\Documents\MijnCSharpProject"

# Stel hier het RELATIEVE pad en de bestandsnaam in voor het outputbestand (t.o.v. $sourceFolder).
# Je kunt ook een volledig pad opgeven, bijv.: "C:\VerzameldeCode\ProjectCode.txt"
$outputFileRelative = "output\allfiles.ps.txt" # Ander achtervoegsel om onderscheid te maken

# Definieer hier de map-patronen (als onderdeel van het pad) die overgeslagen moeten worden.
# Wildcards (*) worden automatisch toegevoegd voor de -like check. Backslashes zijn belangrijk.
$excludeDirPatterns = @(
    '\bin\'
    '\obj\'
    '\packages\'
    '\.idea\'
    '\.vs\'   # Voorbeeld extra patroon
    '\.git\'  # Vaak ook gewenst om uit te sluiten
    '\output\'
)

# Definieer hier de BESTANDSEXTENSIES (incl. punt) die WEL meegenomen moeten worden.
# De check is hoofdletterongevoelig.
[string[]]$allowedExtensions = @(
    '.cs'
    '.csproj'
    '.sln'
    '.json'
    '.config'
    '.resx'
    '.csx'
    '.cpp'
    '.cxx'
    '.cc'
    '.h'
    '.hpp'
    '.hxx'
    '.c'
    '.vcxproj'
    '.rc'
    '.md'
    '.txt'
    '.xml'
    '.yml'
    '.yaml'
    '.sh'
    '.ps1'
    '.py'
    '.js'
    '.html'
    '.css'
    '.sql'
    '.ruleset'
    '.editorconfig'
)

# Definieer hier SPECIFIEKE BESTANDSNAMEN (exact, maar hoofdletterongevoelig) die WEL meegenomen moeten worden.
[string[]]$allowedFilenames = @(
    '.gitignore'
    'README'
    'LICENSE'
    'Dockerfile'
)

#endregion Configuratie

#----------------------------------------------------
#region Script Logica
#----------------------------------------------------

Write-Host "Start script: Code verzamelen" -ForegroundColor Green

# --- Pad Validatie en Voorbereiding ---
if (-not (Test-Path -Path $sourceFolder -PathType Container)) {
    Write-Error "FOUT: Bronmap '$sourceFolder' niet gevonden."
    # Pauzeer alleen als het script direct wordt uitgevoerd, niet in ISE/VSCode Integrated Console
    if ($Host.Name -eq 'ConsoleHost') { Read-Host "Druk op Enter om af te sluiten" }
    exit 1
}

# Bepaal het volledige pad naar het outputbestand
# Controleer of $outputFileRelative een absoluut pad is
if ([System.IO.Path]::IsPathRooted($outputFileRelative)) {
    $outputFile = $outputFileRelative
} else {
    $outputFile = Join-Path -Path $sourceFolder -ChildPath $outputFileRelative
}
$outputDir = Split-Path -Path $outputFile -Parent

Write-Host "Bronmap: $sourceFolder"
Write-Host "Output bestand: $outputFile"

# Maak de output map aan als deze niet bestaat (incl. bovenliggende mappen)
if (-not (Test-Path -Path $outputDir -PathType Container)) {
    Write-Host "Output map '$outputDir' aanmaken..."
    try {
        New-Item -Path $outputDir -ItemType Directory -Force -ErrorAction Stop | Out-Null
    } catch {
        Write-Error "FOUT: Kon output map '$outputDir' niet aanmaken. Fout: $($_.Exception.Message)"
        if ($Host.Name -eq 'ConsoleHost') { Read-Host "Druk op Enter om af te sluiten" }
        exit 1
    }
}

# Verwijder het oude outputbestand als het bestaat
if (Test-Path -Path $outputFile -PathType Leaf) {
    Write-Host "Oud output bestand '$outputFile' wordt verwijderd..."
    Remove-Item -Path $outputFile -Force
}

# --- Schrijf Header naar Output Bestand ---
$header = @"
// Basis map repository: "$sourceFolder"
// Script uitgevoerd op: "$(Get-Date)"
// Bestanden opgenomen op basis van toegestane extensies en bestandsnamen.
// Paden met '$($excludeDirPatterns -join "', '")' worden overgeslagen.

"@ # Hier-string voor multiline text

try {
    Add-Content -Path $outputFile -Value $header -Encoding UTF8 -ErrorAction Stop
} catch {
    Write-Error "FOUT: Kon header niet naar output bestand '$outputFile' schrijven. Fout: $($_.Exception.Message)"
    if ($Host.Name -eq 'ConsoleHost') { Read-Host "Druk op Enter om af te sluiten" }
    exit 1
}

# --- Verwerk Bestanden ---
Write-Host "Bezig met verzamelen van toegestane bestanden..."
$filesProcessed = 0
$filesIncluded = 0

try {
    # Haal alle bestanden recursief op
    Get-ChildItem -Path $sourceFolder -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
        $filesProcessed++
        $currentFile = $_
        $currentFilePath = $currentFile.FullName

        # --- Filter Stappen ---

        # 1. Is het het output bestand zelf?
        if ($currentFilePath -eq $outputFile) {
            Write-Verbose "Bestand overgeslagen (is output bestand): $currentFilePath"
            return # Ga naar het volgende bestand in ForEach-Object (zelfde als 'continue')
        }

        # 2. Bevat het pad een uitgesloten map-patroon?
        $excludeMatch = $false
        foreach ($pattern in $excludeDirPatterns) {
            # Gebruik -like voor wildcard matching (standaard hoofdletterongevoelig)
            # Voeg wildcards toe aan het patroon
            $wildcardPattern = "*$pattern*"
            if ($currentFilePath -like $wildcardPattern) {
                Write-Verbose "Bestand overgeslagen (pad match '$pattern'): $currentFilePath"
                $excludeMatch = $true
                break # Patroon gevonden, stoppen met zoeken naar andere patronen
            }
        }
        if ($excludeMatch) {
            return # Ga naar het volgende bestand
        }

        # 3. Is de extensie of bestandsnaam toegestaan?
        $fileExtension = $currentFile.Extension # Bevat de punt, bijv. ".cs" of ""
        $fileName = $currentFile.Name       # Bevat naam + extensie, bijv. "Program.cs"

        $includeFile = $false
        # De -contains operator is standaard hoofdletterongevoelig voor strings
        if (($allowedExtensions -contains $fileExtension) -or ($allowedFilenames -contains $fileName)) {
            $includeFile = $true
        }

        # --- Bestand Toevoegen aan Output ---
        if ($includeFile) {
            $filesIncluded++
            Write-Verbose "Bestand opnemen: $currentFilePath"

            # Maak de header voor dit bestand (escape $ tekens met backtick `)
            $fileHeader = "// `$``$FILE`$`$: `"$currentFilePath`""

            try {
                # Voeg de bestandsheader toe
                Add-Content -Path $outputFile -Value $fileHeader -Encoding UTF8 -ErrorAction Stop

                # Haal de bestandsinhoud op als één string (-Raw) om problemen met regeleindes te minimaliseren
                $fileContent = Get-Content -Path $currentFilePath -Raw -Encoding UTF8 -ErrorAction Stop # Specificeer UTF8 voor consistentie
                # Voeg de inhoud toe. Add-Content voegt standaard een newline toe na de inhoud.
                Add-Content -Path $outputFile -Value $fileContent -Encoding UTF8 -ErrorAction Stop

                # Voeg extra lege regels toe (Add-Content voegt zelf al een newline toe)
                Add-Content -Path $outputFile -Value "" -Encoding UTF8 -ErrorAction Stop
                Add-Content -Path $outputFile -Value "" -Encoding UTF8 -ErrorAction Stop
                Add-Content -Path $outputFile -Value "" -Encoding UTF8 -ErrorAction Stop

            } catch {
                $errorMessage = "// WARNING: Fout bij lezen/schrijven bestand '$currentFilePath'. Fout: $($_.Exception.Message)"
                Write-Warning $errorMessage
                # Voeg de waarschuwing ook toe aan het output bestand
                Add-Content -Path $outputFile -Value $errorMessage -Encoding UTF8 -ErrorAction SilentlyContinue
            }
        } else {
             Write-Verbose "Bestand overgeslagen (geen match ext/naam): $currentFilePath"
        }
    } # Einde ForEach-Object

} catch {
    # Vang onverwachte fouten tijdens de Get-ChildItem of ForEach loop op
    Write-Error "Onverwachte FOUT tijdens bestandsverwerking: $($_.Exception.Message)"
    # Geef meer details indien beschikbaar
    if ($_.InvocationInfo) {
        Write-Error "Fout opgetreden bij commando: $($_.InvocationInfo.MyCommand)"
        Write-Error "Regel: $($_.InvocationInfo.ScriptLineNumber), Positie: $($_.InvocationInfo.OffsetInLine)"
    }
    if ($Host.Name -eq 'ConsoleHost') { Read-Host "Druk op Enter om af te sluiten" }
    exit 1
}

# --- Afronding ---
Write-Host "Klaar! $filesIncluded van de $filesProcessed gescande bestanden zijn opgenomen in: $outputFile" -ForegroundColor Green

# Open de map waarin het outputbestand staat
Write-Host "Output map '$outputDir' wordt geopend..."
Invoke-Item -Path $outputDir

#endregion Script Logica

Write-Host "Script voltooid."