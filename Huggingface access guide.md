# HuggingFace Pyannote Models Access Guide

## The Problem
If you see an error like:
```
403 Client Error - Access to model pyannote/speaker-diarization-community-1 is restricted
```

This means you need to:
1. Have a HuggingFace account with a valid token
2. Accept the terms for the gated models

**Note:** This project uses `pyannote/speaker-diarization-community-1` for speaker diarization.

## Quick Start (TL;DR)

1. Create a HuggingFace account and get a token at https://huggingface.co/settings/tokens
2. Accept terms at https://huggingface.co/pyannote/speaker-diarization-community-1 and https://huggingface.co/pyannote/wespeaker-voxceleb-resnet34-LM
3. Run `huggingface-cli login` and enter your token
4. Run the app with `uv run python main.py`

For detailed instructions, continue reading.

## Step-by-Step Fix

### Step 1: Create/Get Your HuggingFace Token

1. Go to: https://huggingface.co/settings/tokens
2. If you don't have a token, click "New token"
   - Name: "pyannote-access" (or whatever you like)
   - Type: "Read" is sufficient
   - Copy the token (starts with "hf_...")

### Step 2: Accept Model Terms (REQUIRED!)

Visit each of these URLs and click "Agree and access repository":

**Primary Model (used by this project):**
- https://huggingface.co/pyannote/speaker-diarization-community-1

**Required Dependency Model (voice embeddings):**
- https://huggingface.co/pyannote/wespeaker-voxceleb-resnet34-LM

**Important:** For each model page:
1. Log in to HuggingFace
2. Look for a button like "Agree and access repository" or "Accept license"
3. Click it
4. Wait a few seconds for approval (usually instant)

### Step 3: Set Your Token in Your Environment

You need to set your HuggingFace token where your Python code can find it.

**Option A: Environment Variable (Recommended)**

**Linux/macOS/WSL:**
Add to your shell config (~/.bashrc, ~/.zshrc, or ~/.bash_profile):
```bash
export HF_TOKEN="hf_your_token_here"
```

Or for current session only:
```bash
export HF_TOKEN="hf_your_token_here"
```

**Windows CMD:**
```cmd
set HF_TOKEN=hf_your_token_here
```

**Windows PowerShell:**
```powershell
$env:HF_TOKEN="hf_your_token_here"
```

**Note for WSL users:** Set the environment variable in your WSL environment (Linux shell), not in Windows.

**Option B: huggingface_hub Login (Most Secure)**
```bash
huggingface-cli login
# Enter your token when prompted
# This stores the token in ~/.cache/huggingface/token
```

**Option C: In Your Python Code (Not Recommended for production)**

Add this at the start of your script or in your config:
```python
import os
os.environ["HF_TOKEN"] = "hf_your_token_here"
```

**Warning:** Hardcoding tokens in code is a security risk if you share or commit the code.

### Step 4: Verify Access

Run this Python script to verify:

```python
from huggingface_hub import HfApi, whoami
import os

# Check token
token = os.getenv("HF_TOKEN")
if not token:
    print("❌ No token found!")
    exit(1)

try:
    user = whoami(token=token)
    print(f"✓ Logged in as: {user['name']}")
except:
    print("❌ Invalid token!")
    exit(1)

# Check models (required for this project)
api = HfApi()
models = [
    "pyannote/speaker-diarization-community-1",
    "pyannote/wespeaker-voxceleb-resnet34-LM"
]

for model_id in models:
    try:
        info = api.model_info(model_id, token=token)
        print(f"✓ {model_id} - Access granted")
    except Exception as e:
        print(f"❌ {model_id} - {e}")
```

### Step 5: Update Your Diarization Code

Make sure your diarization pipeline loads with the token.

**In this project**, the token is used in [src/diarization/speaker_diarizer.py](src/diarization/speaker_diarizer.py):

```python
from pyannote.audio import Pipeline
import os

# Load with token (modern syntax)
pipeline = Pipeline.from_pretrained(
    "pyannote/speaker-diarization-community-1",
    token=os.getenv("HF_TOKEN")  # or pass token directly
)

# Note: Older versions used 'use_auth_token=' instead of 'token='
```

## Common Issues

### "403 Forbidden" - Access Denied
- ✓ Make sure you accepted the terms on ALL model pages
- ✓ Wait a few minutes after accepting (caching)
- ✓ Try logging out and back into HuggingFace

### "401 Unauthorized" - Authentication Failed
- ✓ Token not set in environment
- ✓ Token is invalid or expired
- ✓ Create a new token

### "Token not found"
- ✓ Export the token in your current shell session
- ✓ Restart your Python process after setting the token
- ✓ Check you're using the right variable name (HF_TOKEN or HUGGING_FACE_HUB_TOKEN)
- ✓ If using WSL, make sure you set the token in WSL, not in Windows
- ✓ If the token is set but not working, try using `huggingface-cli login` instead

## Quick Test Command

After setting up, test with:

```bash
# Test 1: Token is set
echo $HF_TOKEN

# Test 2: Can access models
python -c "from huggingface_hub import HfApi; import os; api = HfApi(); print(api.model_info('pyannote/speaker-diarization-community-1', token=os.getenv('HF_TOKEN')))"
```

## Where the Token is Used in This Project

The HuggingFace token is used in [src/diarization/speaker_diarizer.py](src/diarization/speaker_diarizer.py) when initializing the pyannote pipeline.

The pipeline is loaded with the `token=` parameter. Make sure your environment variable is set before running the application:

```bash
# Set the token
export HF_TOKEN="hf_your_token_here"

# Run the application
uv run python main.py
```

If the token is not set or invalid, you'll get a 403 or 401 error when trying to perform speaker diarization.