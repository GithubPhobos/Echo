# Dictation history format

History is local, append-only JSONL: one recognized phrase per line, one file per local calendar month.
Audio is not stored. Setup test dictation is excluded. No automatic expiry or upload is performed.
Disabling history stops new entries and keeps the files already written; `/info` still shows their location.

```json
{"ts":"2026-10-31T18:42:03","rt":"Recognized text","held":5.1,"dur":4.25,"rd":0.63,"mdl":"m10","v":"2.0.0","lng":"ru","et":false,"st":0.03,"acc":"cuda","ws":{"t":0.2,"enc":true}}
```

| Field | Meaning |
| --- | --- |
| `ts` | Local date and time, without a time-zone offset |
| `rt` | Recognized text, unchanged |
| `held` | Time the recording key was held, in seconds |
| `dur` | Audio duration after leading-silence trimming, in seconds |
| `rd` | Recognition duration, excluding history I/O and insertion, in seconds |
| `mdl` | Stable model code below; the original model name for an unknown/custom model |
| `v` | Running Echo version |
| `lng` | Configured dictation language; `auto` denotes automatic detection, not a detected language |
| `et` | Translation into English enabled |
| `st` | Recording silence threshold, or null when disabled |
| `acc` | Loaded recognition library: `cpu`, `cuda`, `vulkan`, or null if not known |
| `ws` | Only effective Whisper settings differing from this Echo version's bundled defaults; omitted if empty |

Durations are rounded to hundredths of a second. `held - dur` is only an approximate measure of trimmed/waiting time, not a speech or noise measurement.
`ws` values retain their JSON types. An explicit null differing from a bundled value is retained.
Model, language, translation and requested acceleration are excluded from `ws` because their effective identities already have dedicated fields.
An automatically chosen backend is recorded as the library actually loaded, rather than the word Auto.

## Stable model codes

Optional `ggml-` prefixes and `.bin` extensions are normalized for known models.
Codes are explicit and never reassigned or derived from the order of models in the setup wizard.
Unknown model names are stored in full so distinct custom models remain distinguishable.

| Code | Model |
| --- | --- |
| `m1` | `tiny` |
| `m2` | `tiny.en` |
| `m3` | `base` |
| `m4` | `base.en` |
| `m5` | `small` |
| `m6` | `small.en` |
| `m7` | `medium` |
| `m8` | `medium.en` |
| `m9` | `large-v3-turbo` |
| `m10` | `large-v3` |

## Whisper setting codes inside `ws`

The table is shared by all history entries and is independent of a particular user's choices.
Boolean setting codes begin with `e`. New settings need a new unique code; existing codes must not change meaning.
Settings absent from `ws` have this version's bundled defaults, which are identified by `v`.

| Code | Whisper setting |
| --- | --- |
| `gi` | `GpuDeviceIndex` |
| `th` | `Threads` |
| `enc` | `NoContext` |
| `ess` | `SingleSegment` |
| `est` | `PrintSpecialTokens` |
| `ep` | `PrintProgress` |
| `er` | `PrintResults` |
| `ets` | `PrintTimestamps` |
| `ett` | `UseTokenTimestamps` |
| `esw` | `SplitOnWord` |
| `ecp` | `ComputeProbabilities` |
| `esb` | `SuppressBlank` |
| `p` | `Prompt` |
| `eip` | `CarryInitialPrompt` |
| `sr` | `SuppressRegex` |
| `o` | `Offset` |
| `d` | `Duration` |
| `mlt` | `MaxLastTextTokens` |
| `tt` | `TokenTimestampsThreshold` |
| `tts` | `TokenTimestampsSumThreshold` |
| `msl` | `MaxSegmentLength` |
| `mt` | `MaxTokensPerSegment` |
| `ac` | `AudioContextSize` |
| `t` | `Temperature` |
| `ti` | `TemperatureInc` |
| `it` | `MaxInitialTs` |
| `lp` | `LengthPenalty` |
| `en` | `EntropyThreshold` |
| `lpb` | `LogProbThreshold` |
| `ns` | `NoSpeechThreshold` |
| `ve` | `OpenVinoEncoderPath` |
| `vd` | `OpenVinoDevice` |
| `vc` | `OpenVinoCacheDir` |

Older local history rows may use full field names. Echo does not rewrite or delete existing rows when the format changes.

If an interrupted write leaves a file without its final LF, Echo adds the missing line break before the next phrase. The previous bytes remain unchanged; an incomplete JSON fragment stays on its own invalid line rather than swallowing a later phrase. Only the last byte is read, under the same exclusive lock as the append.
