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
    // Helper function to create the translated dictionary structure
    private static Dictionary<string, string> T(string en, string tr, string de, string es, string it, string fr, string zh, string ru, string ar) => new()
    {
        { "en", en },
        { "tr", tr },
        { "de", de },
        { "es", es },
        { "it", it },
        { "fr", fr },
        { "zh", zh },
        { "ru", ru },
        { "ar", ar }
    };

    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Seeding global variations...");

        // Ensure your ApplicationDbContext has a DbSet<GlobalVariation> named GlobalVariations
        if (await context.GlobalVariations.AnyAsync())
        {
            logger.LogInformation("Global variations already exist. Skipping seeding.");
            return;
        }

        var variations = new List<GlobalVariation>();

        // --- Generic sizes ---
        AddVariations(variations, new Dictionary<string, Dictionary<string, string>>
        {
            { "Small", T("Small", "Küçük", "Klein", "Pequeño", "Piccolo", "Petit", "小份", "Маленький", "صغير") },
            { "Medium", T("Medium", "Orta", "Mittel", "Mediano", "Medio", "Moyen", "中份", "Средний", "وسط") },
            { "Large", T("Large", "Büyük", "Groß", "Grande", "Grande", "Grand", "大份", "Большой", "كبير") },
            { "Extra large", T("Extra large", "Çok büyük", "Extra groß", "Extra grande", "Extra grande", "Très grand", "特大份", "Очень большой", "كبير جداً") },
            { "Regular", T("Regular", "Standart", "Normal", "Normal", "Normale", "Normal", "标准份", "Стандартный", "عادي") },
            { "Mini", T("Mini", "Mini", "Mini", "Mini", "Mini", "Mini", "迷你", "Мини", "ميني") },
            { "Family size", T("Family size", "Aile boy", "Familiengröße", "Tamaño familiar", "Formato famiglia", "Format familial", "家庭装", "Семейный размер", "حجم عائلي") },
            { "Sharing size", T("Sharing size", "Paylaşımlık boy", "Zum Teilen", "Para compartir", "Da condividere", "À partager", "分享装", "Для компании", "حجم للمشاركة") },
        });

        // --- Portions ---
        AddVariations(variations, new Dictionary<string, Dictionary<string, string>>
        {
            { "Half portion", T("Half portion", "Yarım porsiyon", "Halbe Portion", "Media ración", "Mezza porzione", "Demi-portion", "半份", "Половина порции", "نصف حصة") },
            { "Full portion", T("Full portion", "Tam porsiyon", "Ganze Portion", "Ración completa", "Porzione intera", "Portion entière", "整份", "Полная порция", "حصة كاملة") },
            { "Child portion", T("Child portion", "Çocuk porsiyonu", "Kinderportion", "Ración infantil", "Porzione bambino", "Portion enfant", "儿童份", "Детская порция", "حصة أطفال") },
            { "Single", T("Single", "Tekli", "Einfach", "Sencillo", "Singolo", "Simple", "单份", "Одинарный", "مفرد") },
            { "Double", T("Double", "Duble", "Doppelt", "Doble", "Doppio", "Double", "双份", "Двойной", "مزدوج") },
            { "Tasting portion", T("Tasting portion", "Tadımlık porsiyon", "Kostprobe", "Ración de degustación", "Porzione degustazione", "Portion dégustation", "品尝份", "Дегустационная порция", "حصة تذوق") },
        });

        // --- Piece counts ---
        AddVariations(variations, new Dictionary<string, Dictionary<string, string>>
        {
            { "4 pieces", T("4 pieces", "4 parça", "4 Stück", "4 piezas", "4 pezzi", "4 pièces", "4 件", "4 штуки", "4 قطع") },
            { "6 pieces", T("6 pieces", "6 parça", "6 Stück", "6 piezas", "6 pezzi", "6 pièces", "6 件", "6 штук", "6 قطع") },
            { "8 pieces", T("8 pieces", "8 parça", "8 Stück", "8 piezas", "8 pezzi", "8 pièces", "8 件", "8 штук", "8 قطع") },
            { "12 pieces", T("12 pieces", "12 parça", "12 Stück", "12 piezas", "12 pezzi", "12 pièces", "12 件", "12 штук", "12 قطعة") },
            { "20 pieces", T("20 pieces", "20 parça", "20 Stück", "20 piezas", "20 pezzi", "20 pièces", "20 件", "20 штук", "20 قطعة") },
        });

        // --- Drink volumes ---
        AddVariations(variations, new Dictionary<string, Dictionary<string, string>>
        {
            { "20 cl", T("20 cl", "20 cl", "20 cl", "20 cl", "20 cl", "20 cl", "200 毫升", "200 мл", "200 مل") },
            { "25 cl", T("25 cl", "25 cl", "25 cl", "25 cl", "25 cl", "25 cl", "250 毫升", "250 мл", "250 مل") },
            { "33 cl", T("33 cl", "33 cl", "33 cl", "33 cl", "33 cl", "33 cl", "330 毫升", "330 мл", "330 مل") },
            { "50 cl", T("50 cl", "50 cl", "50 cl", "50 cl", "50 cl", "50 cl", "500 毫升", "500 мл", "500 مل") },
            { "75 cl", T("75 cl", "75 cl", "75 cl", "75 cl", "75 cl", "75 cl", "750 毫升", "750 мл", "750 مل") },
            { "1 litre", T("1 litre", "1 litre", "1 Liter", "1 litro", "1 litro", "1 litre", "1 升", "1 литр", "1 لتر") },
        });

        // --- Drink vessels ---
        AddVariations(variations, new Dictionary<string, Dictionary<string, string>>
        {
            { "Glass", T("Glass", "Bardak", "Glas", "Vaso", "Bicchiere", "Verre", "玻璃杯", "Бокал", "كأس") },
            { "Carafe", T("Carafe", "Karaf", "Karaffe", "Jarra", "Caraffa", "Carafe", "壶装", "Графин", "إبريق") },
            { "Bottle", T("Bottle", "Şişe", "Flasche", "Botella", "Bottiglia", "Bouteille", "瓶装", "Бутылка", "زجاجة") },
            { "Half bottle", T("Half bottle", "Yarım şişe", "Halbe Flasche", "Media botella", "Mezza bottiglia", "Demi-bouteille", "半瓶", "Полбутылки", "نصف زجاجة") },
            { "Can", T("Can", "Kutu", "Dose", "Lata", "Lattina", "Canette", "罐装", "Банка", "علبة") },
            { "Cup", T("Cup", "Fincan", "Tasse", "Taza", "Tazza", "Tasse", "杯装", "Чашка", "فنجان") },
            { "Mug", T("Mug", "Kupa", "Becher", "Taza grande", "Tazza grande", "Grande tasse", "大杯", "Кружка", "كوب كبير") },
            { "Pitcher", T("Pitcher", "Sürahi", "Krug", "Jarra grande", "Caraffa grande", "Pichet", "扎壶", "Кувшин", "دورق") },
        });

        // --- Serve temperature ---
        AddVariations(variations, new Dictionary<string, Dictionary<string, string>>
        {
            { "Hot", T("Hot", "Sıcak", "Heiß", "Caliente", "Caldo", "Chaud", "热饮", "Горячий", "ساخن") },
            { "Iced", T("Iced", "Buzlu", "Eisgekühlt", "Con hielo", "Freddo con ghiaccio", "Glacé", "冰镇", "Со льдом", "مثلج") },
        });

        // --- Base, bread and serving format ---
        AddVariations(variations, new Dictionary<string, Dictionary<string, string>>
        {
            { "Thin crust", T("Thin crust", "İnce hamur", "Dünner Boden", "Masa fina", "Impasto sottile", "Pâte fine", "薄底", "Тонкое тесто", "عجينة رقيقة") },
            { "Thick crust", T("Thick crust", "Kalın hamur", "Dicker Boden", "Masa gruesa", "Impasto alto", "Pâte épaisse", "厚底", "Толстое тесто", "عجينة سميكة") },
            { "Gluten-free base", T("Gluten-free base", "Glutensiz hamur", "Glutenfreier Boden", "Base sin gluten", "Base senza glutine", "Pâte sans gluten", "无麸质饼底", "Тесто без глютена", "عجينة خالية من الغلوتين") },
            { "White bread", T("White bread", "Beyaz ekmek", "Weißbrot", "Pan blanco", "Pane bianco", "Pain blanc", "白面包", "Белый хлеб", "خبز أبيض") },
            { "Wholemeal bread", T("Wholemeal bread", "Tam buğday ekmeği", "Vollkornbrot", "Pan integral", "Pane integrale", "Pain complet", "全麦面包", "Цельнозерновой хлеб", "خبز أسمر") },
            { "In a wrap", T("In a wrap", "Dürüm", "Im Wrap", "En wrap", "Nella piadina", "En galette", "卷饼装", "В лаваше", "في لفافة") },
            { "In a bowl", T("In a bowl", "Kase içinde", "In der Bowl", "En bol", "Nella bowl", "En bol", "碗装", "В миске", "في وعاء") },
            { "In a box", T("In a box", "Kutuda", "In der Box", "En caja", "Nella box", "En box", "盒装", "В боксе", "في علبة") },
            { "On a plate", T("On a plate", "Tabakta", "Auf dem Teller", "En plato", "Nel piatto", "À l'assiette", "盘装", "На тарелке", "في طبق") },
        });

        // --- Menu shapes ---
        AddVariations(variations, new Dictionary<string, Dictionary<string, string>>
        {
            { "Menu with fries and drink", T("Menu with fries and drink", "Patates ve içecekli menü", "Menü mit Pommes und Getränk", "Menú con patatas y bebida", "Menu con patatine e bevanda", "Menu avec frites et boisson", "含薯条和饮料套餐", "Меню с картофелем фри и напитком", "وجبة مع بطاطس ومشروب") },
            { "A la carte", T("A la carte", "Alakart", "À la carte", "A la carta", "Alla carta", "À la carte", "单点", "А-ля карт", "حسب الطلب") },
            { "Takeaway", T("Takeaway", "Paket servis", "Zum Mitnehmen", "Para llevar", "Da asporto", "À l'emporter", "外带", "На вынос", "سفري") },
            { "Eat in", T("Eat in", "Restoranda", "Zum Hier-Essen", "Para comer aquí", "Da consumare sul posto", "Sur place", "堂食", "В зале", "تناول في المطعم") },
            { "Combo", T("Combo", "Kombo", "Combo", "Combo", "Combo", "Combo", "套餐", "Комбо", "كومبو") },
            { "Family menu", T("Family menu", "Aile menüsü", "Familienmenü", "Menú familiar", "Menu famiglia", "Menu famille", "家庭套餐", "Семейное меню", "وجبة عائلية") },
        });

        // --- Final Commit ---
        await context.GlobalVariations.AddRangeAsync(variations);
        await context.SaveChangesAsync();

        logger.LogInformation($"Successfully seeded {variations.Count} global variations");
    }

    private static void AddVariations(List<GlobalVariation> list, Dictionary<string, Dictionary<string, string>> variations)
    {
        foreach (var item in variations)
        {
            list.Add(CreateVariation(item.Key, item.Value));
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
