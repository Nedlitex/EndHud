<#
.SYNOPSIS
    Makes a git worktree of EndHud ready to build and test: the test data and the model files in place.

.DESCRIPTION
    A new worktree starts with an empty tests/data and with Git LFS pointer files where the model files
    (src/EndHud.Vision/Assets/ccip) should be: the vision tests then can't build, or fail by the hundred
    on models that can't load. This fills both from the main checkout, without the network:
    - tests/data becomes a real clone of the main checkout's tests/data (objects shared, never a link:
      removing the worktree must not delete anything of the main checkout's), at the commit this
      worktree records, using the main checkout's LFS store, so unpushed recordings are there too;
    - the model files are checked out from the LFS store the worktrees already share.
    Run it from inside the worktree: pwsh scripts/setup-worktree.ps1
#>
$ErrorActionPreference = 'Stop'

$root = (git rev-parse --show-toplevel).Trim()
$common = (Resolve-Path (git rev-parse --git-common-dir).Trim()).Path
$main = Split-Path $common -Parent
if ((Resolve-Path $root).Path -eq (Resolve-Path $main).Path) { throw "This is the main checkout, not a worktree: nothing to set up." }

$mainData = Join-Path $main 'tests/data'
$mainDataGit = Join-Path $common 'modules/tests/data'
$data = Join-Path $root 'tests/data'
$wanted = ((git -C $root ls-tree HEAD tests/data) -split '\s+')[2]
if (-not $wanted) { throw "HEAD records no tests/data commit." }

# A link here would make removing the worktree delete the main checkout's data.
$item = Get-Item $data -Force -ErrorAction SilentlyContinue
if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "tests/data is a link; remove the link (cmd /c rmdir) and run again." }

if (-not (Test-Path (Join-Path $data '.git'))) {
    if ($item) { Remove-Item $data -Recurse -Force }   # the empty submodule folder
    git -c protocol.file.allow=always clone --quiet --shared --no-checkout $mainData $data
    if ($LASTEXITCODE -ne 0) { throw "cloning $mainData failed" }
}
git -C $data config lfs.storage (Join-Path $mainDataGit 'lfs')
git -C $data fetch --quiet $mainData
git -C $data checkout --quiet $wanted
if ($LASTEXITCODE -ne 0) { throw "tests/data: commit $wanted isn't in the main checkout's tests/data" }
git -C $data lfs checkout
git -C $root lfs checkout src/EndHud.Vision/Assets/ccip

# The check that matters: no pointer file left where a model or recording should be.
$pointers = @(Get-ChildItem (Join-Path $root 'src/EndHud.Vision/Assets/ccip'), (Join-Path $data 'Recordings') -File -Recurse |
    Where-Object { $_.Length -lt 200 -and (Get-Content $_.FullName -TotalCount 1 -ErrorAction SilentlyContinue) -like 'version https://git-lfs*' })
if ($pointers.Count) { throw "still LFS pointers (not in the main checkout's LFS store?): $($pointers.Name -join ', ')" }
"tests/data at $wanted; model files and recordings checked out."
