from huggingface_hub import HfApi, whoami
import os
from dotenv import load_dotenv

# Load .env file
load_dotenv()

# Check token
token = os.environ.get("HF_TOKEN")
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
    "pyannote/speaker-diarization-3.1",
    "pyannote/segmentation-3.0"
]

for model_id in models:
    try:
        info = api.model_info(model_id, token=token)
        print(f"✓ {model_id} - Access granted")
    except Exception as e:
        print(f"❌ {model_id} - {e}")