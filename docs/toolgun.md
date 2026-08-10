# Toolgun System

## Кратко

- Toolgun - это не оружие с пулями, а host-authority инструмент для работы с миром через trace.
- Клиент выбирает tool и config в `PropsMenu`, а при ЛКМ/ПКМ отправляет на host: `toolId`, `configJson`, `eyePosition`, `eyeForward`, `attackRange`, `isSecondary`.
- Host сам делает `Scene.Trace.Ray(...)`, валидирует игрока, выбранный tool, текущий toolgun и target, затем вызывает `ToolMode.Use()` или `ToolMode.UseSecondary()`.
- Базовый класс для инструментов - `Minimal.Toolgun.ToolMode`.
- Текущее состояние клиента хранит `ToolgunClientState`: выбранный tool, общий config dictionary, defaults и нормализация числовых/slider значений.

## Основные части

- `WeaponToolgun`:
  - отключает стандартный combat input;
  - не создаёт bullets;
  - обрабатывает `Attack1` и `Attack2`;
  - на host валидирует caller/player/toolgun и выполняет trace;
  - показывает notification только если `ToolUseResult.Message` не пустой.
- `ToolMode`:
  - registry находится в `ToolMode.All`;
  - `RequiresOwnedProp = true` по умолчанию;
  - `SupportsSecondary = false` по умолчанию;
  - `ConfigFields` описывает UI/config tool;
  - `Validate()` по умолчанию требует живого игрока и owned `PropCustom`.
- `ToolUseContext`:
  - `Player` - игрок-владелец toolgun;
  - `Caller` - network connection;
  - `Trace` - host trace result;
  - `TargetProp` - найденный `PropCustom`, если есть;
  - `Config` - parsed JSON config.
- `PropsMenu`:
  - показывает `ToolMode.All`;
  - generic UI умеет `Bool`, `String`, `Number`, `Button`, `Slider`;
  - Textscreen имеет custom UI, потому что там 6 линий, цвета и размер через кнопки.

## Текущие tools

- `RemoverTool`
  - owned `PropCustom`;
  - удаляет prop;
  - должен корректно пройти через `Player.UnregisterSpawnedProp`.
- `ColorTool`
  - owned `PropCustom`;
  - config: `color`;
  - красит через `PropCustom.SetTint`.
- `FadingDoorTool`
  - owned `PropCustom`;
  - ЛКМ добавляет `FadingDoor`, ПКМ удаляет;
  - сама дверь открывается/закрывается через `FadingDoorOpenClose`.
- `NoCollideTool`
  - owned `PropCustom`;
  - ЛКМ включает no-collide с игроками, ПКМ выключает.
- `PushTool`
  - owned `PropCustom`;
  - config: `units` slider `1..50`;
  - ЛКМ двигает prop от игрока по направлению trace, ПКМ тянет обратно;
  - после сдвига замораживает physics;
  - notification намеренно не показывает.
- `TextscreenTool`
  - spawn tool, поэтому `RequiresOwnedProp = false`;
  - config: `background`, 6 линий текста, размер и цвет;
  - создаёт `prefabs/textscreen.prefab`;
  - на объекте настраиваются `Textscreen`, `PropCustom`, physics shell, prop collision tags;
  - занимает prop slot и должен удаляться/двигаться как обычный prop.
- `StackerTool`
  - owned `PropCustom`;
  - config: `stacker.side` (button: up/down/left/right/front/back, default up), `stacker.count` (slider 1..10, default 1), `stacker.gap` (slider 0..100 step 0.25, default 1);
  - ЛКМ создаёт копии пропа по локальным осям в выбранном направлении;
  - копии наследуют model, tint, scale оригинала; спавнятся frozen (`PropPhysicsMode.Frozen`);
  - уважает `Player.MaxProps` — если лимит достигнут, спавнит сколько можно;
  - каждый копия регистрируется через `Player.RegisterSpawnedProp` (доступен Undo и Remover);
  - клиентский ghost-предпросмотр через `StackerGhost` (`code/Toolgun/StackerGhost.cs`): прозрачные non-physical `ModelRenderer`-объекты, видны при наведении на любой `PropCustom`, пропадают при отведении;
  - `StackerGhost.Update()` вызывается из `WeaponToolgun.OnWeaponUpdate()`.

## Подводные камни

- Не доверять клиенту: tool config приходит с клиента, но trace, ownership и действие делает host.
- Для object-tools не обходить `PropCustom`: по умолчанию tool должен работать только по owned `PropCustom`.
- Если tool работает не по prop, явно override `RequiresOwnedProp => false` и сделай свою server-side validation.
- Не выполнять gameplay-логику на клиенте. В `Use()` держать host-only код под `#if SERVER`, если тип/код может попасть в client compilation.
- `ToolUseResult.Ok(null)` означает успешное действие без notification.
- `ToolUseResult.Fail(null)` лучше использовать только для silent fail; для UX обычно возвращай localized message.
- Все config values приходят строками из JSON. Числа парсить invariant culture и clamp на server.
- `ToolgunClientState.Config` общий для всех tools, поэтому id config fields должны быть уникальными и стабильными.
- После добавления нового tool обязательно добавить его в `ToolMode.All`, иначе menu и selection его не увидят.
- Для synced состояния использовать `[Sync(SyncFlags.FromHost)]`, а setter-методы должны ранне выходить, если `!Networking.IsHost`.
- Textscreen UI избегает `TextEntry` для числового размера: в текущем Razor/UI маленький `TextEntry` может некорректно отображать текст, поэтому размер меняется кнопками.
- Если объект должен считаться prop slot, у него должен быть `PropCustom`, владелец, регистрация у `Player` и корректное удаление.
- Для physics/ownership helper-логику лучше переиспользовать (`PropCustom`, `OwnedPropNetwork`, `PropCollisionTags`, `Textscreen.ConfigurePhysicsShell`) вместо копирования.

## Как создать свой tool

1. Создай файл в `code/Toolgun/Tools`, например `MyTool.cs`.
2. Наследуйся от `ToolMode`.
3. Задай metadata:
   - `Id` - стабильный lowercase id;
   - `Title`;
   - `Description`;
   - `SupportsSecondary`, если нужен ПКМ;
   - `RequiresOwnedProp = false`, только если tool не работает по owned `PropCustom`.
4. Опиши config через `ConfigFields`, если он нужен:
   - `ToolConfigType.Bool`;
   - `ToolConfigType.String`;
   - `ToolConfigType.Number`;
   - `ToolConfigType.Button`;
   - `ToolConfigType.Slider`.
5. Реализуй `Use(ToolUseContext context)` для ЛКМ.
6. Реализуй `UseSecondary(ToolUseContext context)`, если включён `SupportsSecondary`.
7. Добавь tool в `ToolMode.All`.
8. Добавь localization keys в `GameLocalizationBuiltIn`, если title/description/config/notifications должны быть переводимыми.
9. Если нужен special UI, добавь отдельную ветку в `PropsMenu`; иначе generic config UI подхватит `ConfigFields`.
10. Проверь, что server action ничего не делает с чужими props и не зависит от client trace.

## Минимальный шаблон

```csharp
namespace Minimal.Toolgun;

public sealed class MyTool : ToolMode
{
	public const string EnabledKey = "mytool.enabled";

	private static readonly IReadOnlyList<ToolConfigField> Fields = new[]
	{
		new ToolConfigField
		{
			Id = EnabledKey,
			Label = "Enabled",
			Type = ToolConfigType.Bool,
			DefaultValue = "false"
		}
	};

	public override string Id => "mytool";
	public override string Title => "My Tool";
	public override string Description => "Does something with your prop.";
	public override IReadOnlyList<ToolConfigField> ConfigFields => Fields;

	public override ToolUseResult Use( ToolUseContext context )
	{
#if SERVER
		var enabled = context.Config.TryGetValue( EnabledKey, out var raw )
			&& string.Equals( raw, "true", StringComparison.OrdinalIgnoreCase );

		// RequiresOwnedProp=true by default, so context.TargetProp is already
		// validated as an owned PropCustom when this method runs.
		var prop = context.TargetProp;

		return ToolUseResult.Ok( null );
#else
		return ToolUseResult.Fail( null );
#endif
	}
}
```

## Проверка перед сдачей

- Не запускать `.NET build`, если это ограничение задачи.
- Статически проверить:
  - tool есть в `ToolMode.All`;
  - config keys уникальны;
  - server-only код закрыт `#if SERVER`, если нужно;
  - object tool не работает по чужим props;
  - notification появляется только когда message не пустой.
- В playtest проверить ЛКМ, ПКМ, чужой prop, отсутствие выбранного tool, config defaults и сетевую синхронизацию.
