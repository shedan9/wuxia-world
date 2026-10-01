"""MiniMax 国内站语音合成的最小客户端（开发期工具，不进游戏包）。

密钥从 voice_source/secrets/minimax.env 读取（已被 .gitignore 排除），
只放进请求头，不打印、不写日志、不写进任何清单。
"""

from __future__ import annotations

import json
import os
import urllib.error
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SECRETS = os.path.join(ROOT, "voice_source", "secrets", "minimax.env")
DEFAULT_HOST = "https://api.minimax.cn"


class MiniMaxError(RuntimeError):
    pass


def _load_env() -> dict[str, str]:
    if not os.path.exists(SECRETS):
        raise MiniMaxError(f"找不到密钥文件 {os.path.relpath(SECRETS, ROOT)}")
    env: dict[str, str] = {}
    with open(SECRETS, encoding="utf-8-sig") as f:
        for raw in f:
            line = raw.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            env[k.strip()] = v.strip().strip('"').strip("'")
    if not env.get("MINIMAX_API_KEY"):
        raise MiniMaxError("密钥文件里没有 MINIMAX_API_KEY")
    return env


class Client:
    def __init__(self) -> None:
        env = _load_env()
        self._key = env["MINIMAX_API_KEY"]
        self.host = env.get("MINIMAX_API_HOST", DEFAULT_HOST).rstrip("/")

    def post(self, path: str, body: dict, timeout: int = 120) -> dict:
        req = urllib.request.Request(
            self.host + path,
            data=json.dumps(body, ensure_ascii=False).encode("utf-8"),
            headers={"Content-Type": "application/json", "Authorization": f"Bearer {self._key}"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                data = json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            raise MiniMaxError(f"HTTP {e.code}：{e.read().decode('utf-8', 'replace')[:300]}") from None
        except urllib.error.URLError as e:
            raise MiniMaxError(f"连接失败：{e.reason}") from None
        base = data.get("base_resp") or {}
        if base.get("status_code", 0) != 0:
            raise MiniMaxError(f"接口返回 {base.get('status_code')}：{base.get('status_msg')}")
        return data

    def voices(self, voice_type: str = "all") -> dict:
        return self.post("/v1/get_voice", {"voice_type": voice_type})

    def synthesize(self, text: str, voice: dict, model: str, audio: dict, pronunciation: list[str] | None = None,
                   modify: dict | None = None) -> tuple[bytes, dict]:
        body: dict = {
            "model": model,
            "text": text,
            "stream": False,
            "voice_setting": voice,
            "audio_setting": audio,
            "output_format": "hex",
            "language_boost": "Chinese",
        }
        if pronunciation:
            body["pronunciation_dict"] = {"tone": pronunciation}
        if modify:
            # 音高 / 力度 / 音色明暗微调，各 [-100, 100]。
            body["voice_modify"] = modify
        data = self.post("/v1/t2a_v2", body)
        audio_hex = (data.get("data") or {}).get("audio")
        if not audio_hex:
            raise MiniMaxError("接口没有返回音频")
        return bytes.fromhex(audio_hex), data.get("extra_info") or {}

    def design(self, prompt: str, preview_text: str) -> tuple[str, bytes]:
        """按文字描述设计音色，返回 voice_id 与试听音频。音色费在该 voice_id 首次用于合成时才扣。"""
        data = self.post("/v1/voice_design", {"prompt": prompt, "preview_text": preview_text})
        voice_id = data.get("voice_id")
        trial = data.get("trial_audio")
        if not voice_id or not trial:
            raise MiniMaxError("接口没有返回 voice_id 或试听音频")
        return voice_id, bytes.fromhex(trial)
