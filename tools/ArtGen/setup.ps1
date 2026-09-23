<#
.SYNOPSIS
  在本机建立 AI 美术生成环境（tools/ArtGen/.venv）并预下载模型。
.DESCRIPTION
  需要 Python 3.12 与 NVIDIA 显卡驱动（支持 CUDA 12.8 及以上）。
  依赖版本锁定在 requirements.txt；PyTorch 单独从 CUDA 12.8 源安装。
  模型缓存默认在 %USERPROFILE%\.cache\huggingface，可用 HF_HOME 改到其他磁盘。
#>
param(
    [string]$Python = 'python',
    [switch]$SkipModels
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$venv = Join-Path $here '.venv'
$py = Join-Path $venv 'Scripts/python.exe'

if (-not (Test-Path $py)) {
    & $Python -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw '创建虚拟环境失败' }
}

& $py -m pip install --upgrade pip
& $py -m pip install -r (Join-Path $here 'requirements-torch.txt') --index-url https://download.pytorch.org/whl/cu128
if ($LASTEXITCODE -ne 0) { throw 'PyTorch 安装失败' }
& $py -m pip install -r (Join-Path $here 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw '依赖安装失败' }

& $py -c "import torch; assert torch.cuda.is_available(), 'CUDA 不可用'; print(torch.__version__, torch.cuda.get_device_name(0))"
if ($LASTEXITCODE -ne 0) { throw 'CUDA 检查失败' }

if (-not $SkipModels) {
    & $py -c @"
from huggingface_hub import snapshot_download
snapshot_download('stabilityai/stable-diffusion-xl-base-1.0', allow_patterns=['*.json', '*.txt', '*fp16.safetensors'])
snapshot_download('madebyollin/sdxl-vae-fp16-fix', allow_patterns=['*.json', '*.safetensors'])
print('模型已缓存')
"@
    if ($LASTEXITCODE -ne 0) { throw '模型下载失败' }
}

Write-Host "环境就绪：$py"
