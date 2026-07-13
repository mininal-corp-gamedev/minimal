# Phone applications

Create application definitions in `Assets/phone/apps` through the s&box Asset Browser:

1. Create a **Phone App Definition** (`.phoneapp`).
2. Set `HeaderEnglish`, `HeaderRussian`, `DescriptionEnglish`, `DescriptionRussian`, `SortOrder`, and `IconPath`. The legacy `Header` and `Description` fields remain fallbacks for older definitions.
3. Add optional `Actions`. Every action must have an ID unique inside the app.
4. For code behavior, implement `IPhoneAppHandler`. Its `AppId` must equal the definition resource ID (normally the file name without `.phoneapp`).

Definitions contain presentation data only. Gameplay mutations must be sent by the handler to a server RPC and validated on the server.

## Localization

Application names and descriptions use `HeaderEnglish`, `HeaderRussian`, `DescriptionEnglish`, and `DescriptionRussian` directly. If a language field is empty, the phone derives localization keys from the application file name. For `<app_id>.phoneapp` and action `<action_id>`, the keys are:

- `phone.app.<app_id>.header`
- `phone.app.<app_id>.description`
- `phone.app.<app_id>.action.<action_id>.header`
- `phone.app.<app_id>.action.<action_id>.description`

Action text continues to use localization keys with values from the definition as fallback. Legacy `Header` and `Description` are retained for old assets.

## Lossless UI icon setup

Put icon sources in `Assets/phone/icons`. Use a square PNG at 256x256 or 512x512 and create a `.vtex` next to it. For pixel-perfect UI icons configure the texture with:

- `OutputMipAlgorithm`: `None`
- `OutputFormat`: `RGBA8888` (do not use lossy `DXT5` for these icons)
- no texture atlas that resizes source images

Then assign the compiled `.vtex` to `IconPath`. The phone renders it with `background-size: contain`. With no mip chain, the engine has no lower-resolution copy to substitute; `RGBA8888` also avoids block-compression artifacts. Keep icons reasonably sized because this deliberately trades VRAM savings for exact source pixels.
