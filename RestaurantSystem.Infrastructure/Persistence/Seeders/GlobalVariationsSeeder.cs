using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders;

/// <summary>
/// Seeds the global variation library.
/// A variation is a sellable variant of one product that changes its price - Small / Medium / Large,
/// 33 cl / 50 cl, 6 pieces / 12 pieces, thin crust / thick crust. It answers the customer question
/// "which one am I buying". It is deliberately NOT a modifier, an ingredient, a sauce or a doneness
/// preference; those belong to their own libraries.
/// The list is intentionally small (about 50 rows) next to the 654-row ingredient library: sizes and
/// formats repeat across an entire menu while prices do not, so a restaurant reuses the same handful
/// of names hundreds of times. The value of this library is therefore the nine translations of each
/// name, not the breadth of the coverage.
/// </summary>
public static class GlobalVariationsSeeder
{
    /// <summary>The library's fallback language — a row's <c>DefaultName</c> is its English name.</summary>
    private const string DefaultLanguage = "en";

    /// <summary>The nine locales every row lists, in the order <see cref="T"/> reads them.</summary>
    private static readonly string[] LanguageCodes =
        [DefaultLanguage, "tr", "de", "es", "it", "fr", "zh", "ru", "ar"];

    // A name a Latin-script locale spells identically in several rows is named once, so the table
    // states it once too.
    private const string OneLitre = "1 litre";
    private const string Combo = "Combo";

    /// <summary>One row: the nine names, in <see cref="LanguageCodes"/> order.</summary>
    private static Dictionary<string, string> T(params string[] names)
    {
        if (names.Length != LanguageCodes.Length)
        {
            throw new ArgumentException(
                $"A variation row needs exactly {LanguageCodes.Length} names, got {names.Length}.",
                nameof(names));
        }

        return LanguageCodes.Zip(names).ToDictionary(pair => pair.First, pair => pair.Second);
    }

    /// <summary>
    /// A row every Latin-script locale spells the same way — a metric volume ("33 cl") or a word
    /// borrowed unchanged ("Mini"). Only zh / ru / ar re-spell it, so only those are typed.
    /// </summary>
    private static Dictionary<string, string> TLatin(string latin, string zh, string ru, string ar) =>
        T(latin, latin, latin, latin, latin, latin, zh, ru, ar);

    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Seeding global variations...");

        if (await context.GlobalVariations.AnyAsync())
        {
            logger.LogInformation("Global variations already exist. Skipping seeding.");
            return;
        }

        var variations = new List<GlobalVariation>();

        // --- Generic sizes ---
        AddVariations(variations,
            T("Small", "Küçük", "Klein", "Pequeño", "Piccolo", "Petit", "小份", "Маленький", "صغير"),
            T("Medium", "Orta", "Mittel", "Mediano", "Medio", "Moyen", "中份", "Средний", "وسط"),
            T("Large", "Büyük", "Groß", "Grande", "Grande", "Grand", "大份", "Большой", "كبير"),
            T("Extra large", "Çok büyük", "Extra groß", "Extra grande", "Extra grande", "Très grand", "特大份", "Очень большой", "كبير جداً"),
            T("Regular", "Standart", "Normal", "Normal", "Normale", "Normal", "标准份", "Стандартный", "عادي"),
            TLatin("Mini", "迷你", "Мини", "ميني"),
            T("Family size", "Aile boy", "Familiengröße", "Tamaño familiar", "Formato famiglia", "Format familial", "家庭装", "Семейный размер", "حجم عائلي"),
            T("Sharing size", "Paylaşımlık boy", "Zum Teilen", "Para compartir", "Da condividere", "À partager", "分享装", "Для компании", "حجم للمشاركة"));

        // --- Portions ---
        AddVariations(variations,
            T("Half portion", "Yarım porsiyon", "Halbe Portion", "Media ración", "Mezza porzione", "Demi-portion", "半份", "Половина порции", "نصف حصة"),
            T("Full portion", "Tam porsiyon", "Ganze Portion", "Ración completa", "Porzione intera", "Portion entière", "整份", "Полная порция", "حصة كاملة"),
            T("Child portion", "Çocuk porsiyonu", "Kinderportion", "Ración infantil", "Porzione bambino", "Portion enfant", "儿童份", "Детская порция", "حصة أطفال"),
            T("Single", "Tekli", "Einfach", "Sencillo", "Singolo", "Simple", "单份", "Одинарный", "مفرد"),
            T("Double", "Duble", "Doppelt", "Doble", "Doppio", "Double", "双份", "Двойной", "مزدوج"),
            T("Tasting portion", "Tadımlık porsiyon", "Kostprobe", "Ración de degustación", "Porzione degustazione", "Portion dégustation", "品尝份", "Дегустационная порция", "حصة تذوق"));

        // --- Piece counts ---
        AddVariations(variations,
            T("4 pieces", "4 parça", "4 Stück", "4 piezas", "4 pezzi", "4 pièces", "4 件", "4 штуки", "4 قطع"),
            T("6 pieces", "6 parça", "6 Stück", "6 piezas", "6 pezzi", "6 pièces", "6 件", "6 штук", "6 قطع"),
            T("8 pieces", "8 parça", "8 Stück", "8 piezas", "8 pezzi", "8 pièces", "8 件", "8 штук", "8 قطع"),
            T("12 pieces", "12 parça", "12 Stück", "12 piezas", "12 pezzi", "12 pièces", "12 件", "12 штук", "12 قطعة"),
            T("20 pieces", "20 parça", "20 Stück", "20 piezas", "20 pezzi", "20 pièces", "20 件", "20 штук", "20 قطعة"));

        // --- Drink volumes ---
        AddVariations(variations,
            TLatin("20 cl", "200 毫升", "200 мл", "200 مل"),
            TLatin("25 cl", "250 毫升", "250 мл", "250 مل"),
            TLatin("33 cl", "330 毫升", "330 мл", "330 مل"),
            TLatin("50 cl", "500 毫升", "500 мл", "500 مل"),
            TLatin("75 cl", "750 毫升", "750 мл", "750 مل"),
            T(OneLitre, OneLitre, "1 Liter", "1 litro", "1 litro", OneLitre, "1 升", "1 литр", "1 لتر"));

        // --- Drink vessels ---
        AddVariations(variations,
            T("Glass", "Bardak", "Glas", "Vaso", "Bicchiere", "Verre", "玻璃杯", "Бокал", "كأس"),
            T("Carafe", "Karaf", "Karaffe", "Jarra", "Caraffa", "Carafe", "壶装", "Графин", "إبريق"),
            T("Bottle", "Şişe", "Flasche", "Botella", "Bottiglia", "Bouteille", "瓶装", "Бутылка", "زجاجة"),
            T("Half bottle", "Yarım şişe", "Halbe Flasche", "Media botella", "Mezza bottiglia", "Demi-bouteille", "半瓶", "Полбутылки", "نصف زجاجة"),
            T("Can", "Kutu", "Dose", "Lata", "Lattina", "Canette", "罐装", "Банка", "علبة"),
            T("Cup", "Fincan", "Tasse", "Taza", "Tazza", "Tasse", "杯装", "Чашка", "فنجان"),
            T("Mug", "Kupa", "Becher", "Taza grande", "Tazza grande", "Grande tasse", "大杯", "Кружка", "كوب كبير"),
            T("Pitcher", "Sürahi", "Krug", "Jarra grande", "Caraffa grande", "Pichet", "扎壶", "Кувшин", "دورق"));

        // --- Serve temperature ---
        AddVariations(variations,
            T("Hot", "Sıcak", "Heiß", "Caliente", "Caldo", "Chaud", "热饮", "Горячий", "ساخن"),
            T("Iced", "Buzlu", "Eisgekühlt", "Con hielo", "Freddo con ghiaccio", "Glacé", "冰镇", "Со льдом", "مثلج"));

        // --- Base, bread and serving format ---
        AddVariations(variations,
            T("Thin crust", "İnce hamur", "Dünner Boden", "Masa fina", "Impasto sottile", "Pâte fine", "薄底", "Тонкое тесто", "عجينة رقيقة"),
            T("Thick crust", "Kalın hamur", "Dicker Boden", "Masa gruesa", "Impasto alto", "Pâte épaisse", "厚底", "Толстое тесто", "عجينة سميكة"),
            T("Gluten-free base", "Glutensiz hamur", "Glutenfreier Boden", "Base sin gluten", "Base senza glutine", "Pâte sans gluten", "无麸质饼底", "Тесто без глютена", "عجينة خالية من الغلوتين"),
            T("White bread", "Beyaz ekmek", "Weißbrot", "Pan blanco", "Pane bianco", "Pain blanc", "白面包", "Белый хлеб", "خبز أبيض"),
            T("Wholemeal bread", "Tam buğday ekmeği", "Vollkornbrot", "Pan integral", "Pane integrale", "Pain complet", "全麦面包", "Цельнозерновой хлеб", "خبز أسمر"),
            T("In a wrap", "Dürüm", "Im Wrap", "En wrap", "Nella piadina", "En galette", "卷饼装", "В лаваше", "في لفافة"),
            T("In a bowl", "Kase içinde", "In der Bowl", "En bol", "Nella bowl", "En bol", "碗装", "В миске", "في وعاء"),
            T("In a box", "Kutuda", "In der Box", "En caja", "Nella box", "En box", "盒装", "В боксе", "في علبة"),
            T("On a plate", "Tabakta", "Auf dem Teller", "En plato", "Nel piatto", "À l'assiette", "盘装", "На тарелке", "في طبق"));

        // --- Menu shapes ---
        AddVariations(variations,
            T("Menu with fries and drink", "Patates ve içecekli menü", "Menü mit Pommes und Getränk", "Menú con patatas y bebida", "Menu con patatine e bevanda", "Menu avec frites et boisson", "含薯条和饮料套餐", "Меню с картофелем фри и напитком", "وجبة مع بطاطس ومشروب"),
            T("A la carte", "Alakart", "À la carte", "A la carta", "Alla carta", "À la carte", "单点", "А-ля карт", "حسب الطلب"),
            T("Takeaway", "Paket servis", "Zum Mitnehmen", "Para llevar", "Da asporto", "À l'emporter", "外带", "На вынос", "سفري"),
            T("Eat in", "Restoranda", "Zum Hier-Essen", "Para comer aquí", "Da consumare sul posto", "Sur place", "堂食", "В зале", "تناول في المطعم"),
            T(Combo, "Kombo", Combo, Combo, Combo, Combo, "套餐", "Комбо", "كومبو"),
            T("Family menu", "Aile menüsü", "Familienmenü", "Menú familiar", "Menu famiglia", "Menu famille", "家庭套餐", "Семейное меню", "وجبة عائلية"));

        await context.GlobalVariations.AddRangeAsync(variations);
        await context.SaveChangesAsync();

        logger.LogInformation("Successfully seeded {VariationCount} global variations", variations.Count);
    }

    private static void AddVariations(List<GlobalVariation> list, params Dictionary<string, string>[] rows)
    {
        foreach (var row in rows)
        {
            list.Add(CreateVariation(row[DefaultLanguage], row));
        }
    }

    private static GlobalVariation CreateVariation(string defaultName, Dictionary<string, string> translations)
    {
        return new GlobalVariation
        {
            DefaultName = defaultName,
            IsActive = true,
            CreatedBy = "System",
            Translations = translations.Select(t => new GlobalVariationTranslation
            {
                LanguageCode = t.Key,
                Name = t.Value,
                CreatedBy = "System"
            }).ToList()
        };
    }
}
