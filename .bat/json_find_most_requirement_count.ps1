# Define the folder path
$folderPath = "..\JSON\class"

# Initialize a variable to store the result
$maxCount = 0
$maxFile = ""

# Get all files in the folder
$files = Get-ChildItem -Path $folderPath -File

# Iterate through each file
foreach ($file in $files) {
    # Read the file content
    $content = Get-Content -Path $file.FullName
    
    # Count occurrences of the word "Requirement"
    $count = [regex]::Matches($content, "Requirement", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase).Count
    
    # Check if this file has more occurrences than the current maximum
    if ($count -gt $maxCount) {
        $maxCount = $count
        $maxFile = $file.FullName
    }
}

# Output the result
if ($maxFile -ne "") {
    Write-Output "File with the most occurrences of 'Requirement': $maxFile"
    Write-Output "Occurrences count: $maxCount"
} else {
    Write-Output "No files found in the specified folder."
}