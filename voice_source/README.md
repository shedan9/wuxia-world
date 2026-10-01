# 配音源

台词清单、音色档案、发音词典与已审核母带；音频经 Git LFS 管理。供应商密钥不得入库。

- `profiles/`：音色档案（说话人 → 供应商音色 ID 与语速、音高等参数）。`minimax_trial.json`（第一轮）与 `minimax_natural.json`（第二轮，按用户“更真实平常、不要表演味”调整）是 2026-10-01 的试听用临时档案，借用系统音色，不是正式音色；`minimax_round3.json` 是第三轮微调候选（键名 `人物ID@候选名`）。`minimax_cast.json` 是用户试听后的工作选角（非最终定稿，待定角色列在 `pending`）。`minimax_design_v1.json`、`minimax_design_v2.json` 是音色设计描述，设计结果（`voice_id`）在 `build/voice/design/` 下，未经用户同意不用于合成。
- `secrets/`：本机密钥（如 `minimax.env`），被 `.gitignore` 排除。
