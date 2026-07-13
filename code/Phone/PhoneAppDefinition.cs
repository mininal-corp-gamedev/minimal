using System.Collections.Generic;

namespace Minimal.PhoneSystem;

[AssetType( Name = "Phone App Definition", Extension = "phoneapp", Category = "Minimal" )]
public sealed class PhoneAppDefinition : GameResource
{
	public string Id => ResourceName;

	[Property, Description( "Название под иконкой и на странице приложения." )]
	public string Header { get; set; } = "Application";

	[Property, Description( "Текст на стандартной странице приложения." )]
	public string Description { get; set; } = "";

	[Property, Group( "Localization" ), Description( "Название приложения на английском языке." )]
	public string HeaderEnglish { get; set; } = "";

	[Property, Group( "Localization" ), Description( "Название приложения на русском языке." )]
	public string HeaderRussian { get; set; } = "";

	[Property, Group( "Localization" ), Description( "Описание приложения на английском языке." )]
	public string DescriptionEnglish { get; set; } = "";

	[Property, Group( "Localization" ), Description( "Описание приложения на русском языке." )]
	public string DescriptionRussian { get; set; } = "";

	[Property, ResourceType( "vtex" ), Description( "Иконка приложения. Рекомендуемый размер исходника: 256x256 или 512x512." )]
	public string IconPath { get; set; } = "";

	[Property, Description( "Символ, который показывается, если IconPath не задан." )]
	public string FallbackGlyph { get; set; } = "?";

	[Property, Description( "Цвет квадратной подложки иконки в CSS-формате." )]
	public string IconBackground { get; set; } = "#394250";

	[Property, Description( "Меньшее число располагает приложение раньше." )]
	public int SortOrder { get; set; } = 100;

	[Property] public bool Enabled { get; set; } = true;

	[Property, Description( "Показывать стандартную страницу после нажатия на иконку." )]
	public bool ShowPage { get; set; } = true;

	[Property, Description( "Закрыть телефон сразу после открытия приложения. Удобно, если handler открывает отдельный интерфейс." )]
	public bool ClosePhoneOnOpen { get; set; } = false;

	[Property] public List<PhoneAppActionDefinition> Actions { get; set; } = new();

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "smartphone", width, height, "#ffffff", "#ff436c" );
	}
}

public sealed class PhoneAppActionDefinition
{
	[Property, Description( "Уникальный ID действия внутри приложения." )]
	public string Id { get; set; } = "action";

	[Property] public string Header { get; set; } = "Action";
	[Property] public string Description { get; set; } = "";
	[Property] public bool ClosePhoneOnUse { get; set; } = false;
}
