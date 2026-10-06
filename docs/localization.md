# Interface languages

Choose **View → Language → English / 简体中文 / 日本語** to switch the interface immediately. The choice is saved per library in `settings.json`. On first use, the interface follows the Windows UI language when supported, otherwise English.

The main menus, toolbar, editor labels, text editing commands, relation choices, reading-role controls and common notifications are translated. Some detailed validation diagnostics and technical documentation still use English. Native file dialogs follow Windows settings.

Changing the interface language does not translate manuscript text, graph titles, category names or notes. The graph format, stored relation identifiers, agent JSON interface and Markdown output remain compatible.

## Translation resources

`Locales/en.json` contains English source strings; `zh-CN.json` and `ja.json` contain translations with the same keys. Missing strings fall back to their English source. Preserve numeric format placeholders such as `{0}` and `{1}`, shortcut notation, and the symbols □ and ◎.

For an additional language, add an embedded JSON catalog, register its code and native display name in `Localization.Languages`, and extend catalog loading and culture normalization in `Localization.cs`. Use `Localization.Bind` / dynamic resources for controls that stay visible during switching; use `Localization.Text` or `Format` when rebuilding transient UI. Never apply localization to user-authored text or stored graph identifiers.

Run the existing `--self-test` suite after translation changes. It includes switching without restart, settings preservation, startup fallback, unchanged graph content and screenshots of all three interfaces.
