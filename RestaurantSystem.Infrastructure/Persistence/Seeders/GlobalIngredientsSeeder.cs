using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders;

public static class GlobalIngredientsSeeder
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
        logger.LogInformation("Seeding global ingredients...");

        // Ensure your ApplicationDbContext has a DbSet<GlobalIngredient> named GlobalIngredients
        if (await context.GlobalIngredients.AnyAsync())
        {
            logger.LogInformation("Global ingredients already exist. Skipping seeding.");
            return;
        }

        var ingredients = new List<GlobalIngredient>();

        // --- Flours, Starches, and Leavening ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "All-purpose flour", T("All-purpose flour", "Çok amaçlı un", "Allzweckmehl", "Harina de trigo común", "Farina 00", "Farine tout usage", "中筋面粉", "Универсальная мука", "دقيق متعدد الاستعمالات") },
            { "Bread flour", T("Bread flour", "Ekmeklik un", "Brotmehl", "Harina de fuerza", "Farina per pane", "Farine à pain", "高筋面粉", "Хлебная мука", "دقيق الخبز") },
            { "00 flour", T("00 flour", "00 numara un", "Type 00 Mehl", "Harina 00", "Farina 00", "Farine type 00", "00号面粉", "Мука 00", "دقيق 00") },
            { "Whole wheat flour", T("Whole wheat flour", "Tam buğday unu", "Vollkornmehl", "Harina integral", "Farina integrale", "Farine de blé entier", "全麦面粉", "Цельнозерновая мука", "دقيق القمح الكامل") },
            { "Semolina", T("Semolina", "İrmik", "Grieß", "Sémola", "Semolino", "Semoule", "粗面粉", "Манная крупа", "سميد") },
            { "Rice flour", T("Rice flour", "Pirinç unu", "Reismehl", "Harina de arroz", "Farina di riso", "Farine de riz", "米粉", "Рисовая мука", "دقيق الأرز") },
            { "Corn flour", T("Corn flour", "Mısır unu", "Maismehl", "Harina de maíz", "Farina di mais", "Farine de maïs", "玉米粉", "Кукурузная мука", "دقيق الذرة") },
            { "Cornstarch", T("Cornstarch", "Mısır nişastası", "Maisstärke", "Maicena", "Amido di mais", "Fécule de maïs", "玉米淀粉", "Кукурузный крахмал", "نشا الذرة") },
            { "Baking powder", T("Baking powder", "Kabartma tozu", "Backpulver", "Polvo de hornear", "Lievito per dolci", "Levure chimique", "泡打粉", "Разрыхлитель", "مسحوق الخبز") },
            { "Baking soda", T("Baking soda", "Karbonat", "Natron", "Bicarbonato de sodio", "Bicarbonato di sodio", "Bicarbonate de soude", "小苏打", "Пищевая сода", "بيكربونات الصوديوم") },
            { "Yeast", T("Yeast", "Maya", "Hefe", "Levadura", "Lievito", "Levure", "酵母", "Дрожжи", "خميرة") },
            { "Active dry yeast", T("Active dry yeast", "Aktif kuru maya", "Aktive Trockenhefe", "Levadura seca activa", "Lievito secco attivo", "Levure sèche active", "活性干酵母", "Активные сухие дрожжи", "خميرة جافة نشطة") },
            { "Fresh yeast", T("Fresh yeast", "Yaş maya", "Frischhefe", "Levadura fresca", "Lievito fresco", "Levure fraîche", "鲜酵母", "Свежие дрожжи", "خميرة طازجة") },
            { "Instant yeast", T("Instant yeast", "İnstant maya", "Instanthefe", "Levadura instantánea", "Lievito istantaneo", "Levure instantanée", "速溶酵母", "Мгновенные дрожжи", "خميرة فورية") },
            { "Sourdough starter", T("Sourdough starter", "Ekşi maya", "Sauerteigstarter", "Masa madre", "Lievito madre", "Levain", "酵头", "Закваска", "خميرة طبيعية") },
        });

        // --- Salts and Sweeteners ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Salt", T("Salt", "Tuz", "Salz", "Sal", "Sale", "Sel", "盐", "Соль", "ملح") },
            { "Sea salt", T("Sea salt", "Deniz tuzu", "Meersalz", "Sal marina", "Sale marino", "Sel marin", "海盐", "Морская соль", "ملح بحري") },
            { "Kosher salt", T("Kosher salt", "Koşer tuzu", "Koscheres Salz", "Sal kosher", "Sale kosher", "Sel kasher", "犹太盐", "Кошерная соль", "ملح كوشير") },
            { "Black salt", T("Black salt", "Siyah tuz", "Schwarzsalz", "Sal negra", "Sale nero", "Sel noir", "黑盐", "Черная соль", "ملح أسود") },
            { "Pink himalayan salt", T("Pink himalayan salt", "Pembe himalaya tuzu", "Rosa Himalaya-Salz", "Sal rosa del Himalaya", "Sale rosa dell'Himalaya", "Sel rose de l'Himalaya", "粉红喜马拉雅盐", "Розовая гималайская соль", "ملح الهيمالايا الوردي") },
            { "Sugar", T("Sugar", "Şeker", "Zucker", "Azúcar", "Zucchero", "Sucre", "糖", "Сахар", "سكر") },
            { "Brown sugar", T("Brown sugar", "Esmer şeker", "Brauner Zucker", "Azúcar moreno", "Zucchero di canna", "Cassonade", "红糖", "Коричневый сахар", "سكر بني") },
            { "Powdered sugar", T("Powdered sugar", "Pudra şekeri", "Puderzucker", "Azúcar glas", "Zucchero a velo", "Sucre glace", "糖粉", "Сахарная пудра", "سكر بودرة") },
            { "Molasses", T("Molasses", "Pekmez", "Melasse", "Melaza", "Melassa", "Mélasse", "糖蜜", "Меласса", "دبس") },
            { "Honey", T("Honey", "Bal", "Honig", "Miel", "Miele", "Miel", "蜂蜜", "Мед", "عسل") },
            { "Date syrup", T("Date syrup", "Hurma şurubu", "Dattelsirup", "Sirope de dátil", "Sciroppo di datteri", "Sirop de datte", "枣糖浆", "Финиковый сироп", "دبس التمر") },
            { "Grape molasses", T("Grape molasses", "Üzüm pekmezi", "Traubenmelasse", "Melaza de uva", "Melassa d'uva", "Mélasse de raisin", "葡萄糖蜜", "Виноградная меласса", "دبس العنب") },
            { "Pomegranate molasses", T("Pomegranate molasses", "Nar ekşisi", "Granatapfelmelasse", "Melaza de granada", "Melassa di melograno", "Mélasse de grenade", "石榴糖蜜", "Гранатовая меласса", "دبس الرمان") },
            { "Maple syrup", T("Maple syrup", "Akçaağaç şurubu", "Ahornsirup", "Sirope de arce", "Sciroppo d'acero", "Sirop d'érable", "枫糖浆", "Кленовый сироп", "شراب القيقب") },
            { "Agave syrup", T("Agave syrup", "Agave şurubu", "Agavendicksaft", "Sirope de agave", "Sciroppo d'agave", "Sirop d'agave", "龙舌兰糖浆", "Сироп агавы", "شراب الصبار") },
            { "Glucose syrup", T("Glucose syrup", "Glikoz şurubu", "Glukosesirup", "Sirope de glucosa", "Sciroppo di glucosio", "Sirop de glucose", "葡萄糖浆", "Глюкозный сироп", "شراب الجلوكوز") },
        });

        // --- Oils and Fats ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Olive oil", T("Olive oil", "Zeytinyağı", "Olivenöl", "Aceite de oliva", "Olio d'oliva", "Huile d'olive", "橄榄油", "Оливковое масло", "زيت الزيتون") },
            { "Extra virgin olive oil", T("Extra virgin olive oil", "Sızma zeytinyağı", "Natives Olivenöl extra", "Aceite de oliva virgen extra", "Olio extra vergine d'oliva", "Huile d'olive vierge extra", "特级初榨橄榄油", "Оливковое масло первого отжима", "زيت الزيتون البكر الممتاز") },
            { "Sunflower oil", T("Sunflower oil", "Ayçiçek yağı", "Sonnenblumenöl", "Aceite de girasol", "Olio di semi di girasole", "Huile de tournesol", "葵花籽油", "Подсолнечное масло", "زيت عباد الشمس") },
            { "Corn oil", T("Corn oil", "Mısır yağı", "Maisöl", "Aceite de maíz", "Olio di mais", "Huile de maïs", "玉米油", "Кукурузное масло", "زيت الذرة") },
            { "Avocado oil", T("Avocado oil", "Avokado yağı", "Avocadoöl", "Aceite de aguacate", "Olio di avocado", "Huile d'avocat", "牛油果油", "Масло авокадо", "زيت الأفوكادو") },
            { "Grapeseed oil", T("Grapeseed oil", "Üzüm çekirdeği yağı", "Traubenkernöl", "Aceite de semilla de uva", "Olio di vinaccioli", "Huile de pépins de raisin", "葡萄籽油", "Масло виноградных косточек", "زيت بذور العنب") },
            { "Sesame oil", T("Sesame oil", "Susam yağı", "Sesamöl", "Aceite de sésamo", "Olio di sesamo", "Huile de sésame", "芝麻油", "Кунжутное масло", "زيت السمسم") },
            { "Toasted sesame oil", T("Toasted sesame oil", "Kavrulmuş susam yağı", "Geröstetes Sesamöl", "Aceite de sésamo tostado", "Olio di sesamo tostato", "Huile de sésame grillé", "烤芝麻油", "Жареное кунжутное масло", "زيت السمسم المحمص") },
            { "Butter", T("Butter", "Tereyağı", "Butter", "Mantequilla", "Burro", "Beurre", "黄油", "Сливочное масло", "زبدة") },
            { "Clarified butter", T("Clarified butter", "Sade yağ", "Geklärte Butter", "Mantequilla clarificada", "Burro chiarificato", "Beurre clarifié", "澄清黄油", "Топленое масло", "زبدة مصفاة") },
            { "Ghee", T("Ghee", "Ghee", "Ghee", "Ghee", "Ghee", "Ghee", "酥油", "Ги", "سمن") },
            { "Margarine", T("Margarine", "Margarin", "Margarine", "Margarina", "Margarina", "Margarine", "人造黄油", "Маргарин", "مارجرين") },
            { "Vegetable oil", T("Vegetable oil", "Bitkisel yağ", "Pflanzenöl", "Aceite vegetal", "Olio vegetale", "Huile végétale", "植物油", "Растительное масло", "زيت نباتي") },
            { "Beef tallow", T("Beef tallow", "Dana iç yağı", "Rindertalg", "Sebo de res", "Seo di manzo", "Suif de bœuf", "牛脂", "Говяжий жир", "دهن البقر") },
            { "Lamb fat", T("Lamb fat", "Kuzu yağı", "Lammfett", "Grasa de cordero", "Grasso d'agnello", "Graisse d'agneau", "羊脂", "Бараний жир", "دهن الضأن") },
            { "Duck fat", T("Duck fat", "Ördek yağı", "Entenfett", "Grasa de pato", "Grasso d'anatra", "Graisse de canard", "鸭油", "Утиный жир", "دهن البط") },
            { "Chicken fat", T("Chicken fat", "Tavuk yağı", "Hühnerfett", "Grasa de pollo", "Grasso di pollo", "Graisse de poulet", "鸡油", "Куриный жир", "دهن الدجاج") },
        });

        // --- Vegetables: Fruits, Peppers, and Onions ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Tomatoes", T("Tomatoes", "Domates", "Tomaten", "Tomates", "Pomodori", "Tomates", "番茄", "Помидоры", "طماطم") },
            { "Cherry tomatoes", T("Cherry tomatoes", "Çeri domates", "Kirschtomaten", "Tomates cherry", "Pomodorini", "Tomates cerises", "樱桃番茄", "Помидоры черри", "طماطم كرزية") },
            { "Roma tomatoes", T("Roma tomatoes", "Roma domatesi", "Roma-Tomaten", "Tomates Roma", "Pomodori Roma", "Tomates Roma", "罗马番茄", "Помидоры Рома", "طماطم روما") },
            { "Sun-dried tomatoes", T("Sun-dried tomatoes", "Güneşte kurutulmuş domates", "Getrocknete Tomaten", "Tomates secos", "Pomodori secchi", "Tomates séchées", "干番茄", "Вяленые помидоры", "طماطم مجففة") },
            { "Green peppers", T("Green peppers", "Yeşil biber", "Grüne Paprika", "Pimientos verdes", "Peperoni verdi", "Poivrons verts", "青椒", "Зеленый перец", "فلفل أخضر") },
            { "Red peppers", T("Red peppers", "Kırmızı biber", "Rote Paprika", "Pimientos rojos", "Peperoni rossi", "Poivrons rouges", "红椒", "Красный перец", "فلفل أحمر") },
            { "Yellow peppers", T("Yellow peppers", "Sarı biber", "Gelbe Paprika", "Pimientos amarillos", "Peperoni gialli", "Poivrons jaunes", "黄椒", "Желтый перец", "فلفل أصفر") },
            { "Orange peppers", T("Orange peppers", "Turuncu biber", "Orange Paprika", "Pimientos naranjas", "Peperoni arancioni", "Poivrons oranges", "橙椒", "Оранжевый перец", "فلفل برتقالي") },
            { "Bell peppers", T("Bell peppers", "Dolmalık biber", "Glockenpaprika", "Pimientos morrones", "Peperoni", "Poivrons doux", "灯笼椒", "Болгарский перец", "فلفل رومي") },
            { "Spicy green peppers", T("Spicy green peppers", "Acı yeşil biber", "Scharfe grüne Paprika", "Pimientos verdes picantes", "Peperoncini verdi", "Piments verts", "辣青椒", "Острый зеленый перец", "فلفل أخضر حار") },
            { "Jalapeño", T("Jalapeño", "Jalapeno", "Jalapeño", "Jalapeño", "Jalapeño", "Piment Jalapeño", "墨西哥辣椒", "Халапеньо", "هالبينو") },
            { "Serrano pepper", T("Serrano pepper", "Serrano biberi", "Serrano-Pfeffer", "Chile serrano", "Peperoncino Serrano", "Piment Serrano", "塞拉诺辣椒", "Перец Серрано", "فلفل سيرانو") },
            { "Habanero", T("Habanero", "Habanero", "Habanero", "Habanero", "Habanero", "Piment Habanero", "哈瓦那辣椒", "Хабанеро", "هابانيرو") },
            { "Anaheim pepper", T("Anaheim pepper", "Anaheim biberi", "Anaheim-Pfeffer", "Chile Anaheim", "Peperoncino Anaheim", "Piment Anaheim", "阿纳海姆辣椒", "Перец Анахайм", "فلفل أنهايم") },
            { "Poblano pepper", T("Poblano pepper", "Poblano biberi", "Poblano-Pfeffer", "Chile poblano", "Peperoncino Poblano", "Piment Poblano", "波布拉诺辣椒", "Перец Поблано", "فلفل بوبلانو") },
            { "Banana pepper", T("Banana pepper", "Muz biberi", "Bananenpfeffer", "Pimiento banana", "Peperone banana", "Piment banane", "香蕉辣椒", "Банановый перец", "فلفل الموز") },
            { "Pickled peppers", T("Pickled peppers", "Turşu biber", "Eingelegte Paprika", "Pimientos encurtidos", "Peperoni sott'aceto", "Piments marinés", "腌辣椒", "Маринованный перец", "فلفل مخلل") },
            { "Pickled jalapeños", T("Pickled jalapeños", "Turşu jalapeno", "Eingelegte Jalapeños", "Jalapeños encurtidos", "Jalapeño sott'aceto", "Jalapeños marinés", "腌墨西哥辣椒", "Маринованный халапеньо", "هالبينو مخلل") },
            { "Onions", T("Onions", "Soğan", "Zwiebeln", "Cebollas", "Cipolle", "Oignons", "洋葱", "Лук", "بصل") },
            { "Yellow onions", T("Yellow onions", "Sarı soğan", "Gelbe Zwiebeln", "Cebollas amarillas", "Cipolle dorate", "Oignons jaunes", "黄洋葱", "Желтый лук", "بصل أصفر") },
            { "Red onions", T("Red onions", "Kırmızı soğan", "Rote Zwiebeln", "Cebollas rojas", "Cipolle rosse", "Oignons rouges", "红洋葱", "Красный лук", "بصل أحمر") },
            { "White onions", T("White onions", "Beyaz soğan", "Weiße Zwiebeln", "Cebollas blancas", "Cipolle bianche", "Oignons blancs", "白洋葱", "Белый лук", "بصل أبيض") },
            { "Spring onions", T("Spring onions", "Taze soğan", "Frühlingszwiebeln", "Cebolletas", "Cipollotti", "Oignons de printemps", "葱", "Зеленый лук", "بصل أخضر") },
            { "Shallots", T("Shallots", "Arpacık soğan", "Schalotten", "Chalotas", "Scalogni", "Échalotes", "青葱", "Шаллот", "كراث") },
            { "Scallions", T("Scallions", "Yeşil soğan", "Lauchzwiebeln", "Cebolletas", "Cipollotti", "Ciboule", "细香葱", "Шнитт-лук", "بصل الربيع") },
            { "Garlic", T("Garlic", "Sarımsak", "Knoblauch", "Ajo", "Aglio", "Ail", "大蒜", "Чеснок", "ثوم") },
            { "Black garlic", T("Black garlic", "Siyah sarımsak", "Schwarzer Knoblauch", "Ajo negro", "Aglio nero", "Ail noir", "黑蒜", "Черный чеснок", "ثوم أسود") },
            { "Ginger", T("Ginger", "Zencefil", "Ingwer", "Jengibre", "Zenzero", "Gingembre", "姜", "Имбирь", "زنجبيل") },
        });

        // --- Vegetables: Roots, Stems, and Pods ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Celery", T("Celery", "Kereviz sapı", "Sellerie", "Apio", "Sedano", "Céleri", "芹菜", "Сельдерей", "كرفس") },
            { "Celery leaves", T("Celery leaves", "Kereviz yaprakları", "Sellerieblätter", "Hojas de apio", "Foglie di sedano", "Feuilles de céleri", "芹菜叶", "Листья сельдерея", "أوراق الكرفس") },
            { "Leeks", T("Leeks", "Pırasa", "Lauch", "Puerros", "Porri", "Poireaux", "韭菜", "Лук-порей", "كراث") },
            { "Carrots", T("Carrots", "Havuç", "Karotten", "Zanahorias", "Carote", "Carottes", "胡萝卜", "Морковь", "جزر") },
            { "Beets", T("Beets", "Pancar", "Rüben", "Remolachas", "Barbabietole", "Betteraves", "甜菜", "Свекла", "شمندر") },
            { "Turnips", T("Turnips", "Şalgam", "Rüben", "Nabos", "Rape", "Navets", "芜菁", "Репа", "لفت") },
            { "Radish", T("Radish", "Turp", "Radieschen", "Rábanos", "Ravanelli", "Radis", "萝卜", "Редис", "فجل") },
            { "Horseradish", T("Horseradish", "Bayır turpu", "Meerrettich", "Rábano picante", "Rafano", "Raifort", "辣根", "Хрен", "فجل حار") },
            { "Parsnip", T("Parsnip", "Yabani havuç", "Pastinake", "Chirivía", "Pastinaca", "Panais", "欧洲防风", "Пастернак", "جزر أبيض") },
            { "Fennel", T("Fennel", "Rezene", "Fenchel", "Hinojo", "Finocchio", "Fenouil", "茴香", "Фенхель", "شمر") },
            { "Zucchini", T("Zucchini", "Kabak", "Zucchini", "Calabacín", "Zucchine", "Courgette", "西葫芦", "Кабачок", "كوسا") },
            { "Eggplant", T("Eggplant", "Patlıcan", "Aubergine", "Berenjena", "Melanzana", "Aubergine", "茄子", "Баклажан", "باذنجان") },
            { "Broccoli", T("Broccoli", "Brokoli", "Brokkoli", "Brócoli", "Broccoli", "Brocoli", "西兰花", "Брокколи", "بروكلي") },
            { "Cauliflower", T("Cauliflower", "Karnabahar", "Blumenkohl", "Coliflor", "Cavolfiore", "Chou-fleur", "花椰菜", "Цветная капуста", "قرنبيط") },
            { "Okra", T("Okra", "Bamya", "Okra", "Okra", "Okra", "Gombo", "秋葵", "Окра", "بامية") },
            { "Artichokes", T("Artichokes", "Enginar", "Artischocken", "Alcachofas", "Carciofi", "Artichauts", "朝鲜蓟", "Артишоки", "خرشوف") },
        });

        // --- Vegetables: Fungi and Greens ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Mushrooms", T("Mushrooms", "Mantarlar", "Pilze", "Champiñones", "Funghi", "Champignons", "蘑菇", "Грибы", "فطر") },
            { "Button mushrooms", T("Button mushrooms", "Kültür mantarı", "Champignons", "Champiñones blancos", "Funghi champignon", "Champignons de Paris", "口蘑", "Шампиньоны", "فطر أزرار") },
            { "Portobello mushrooms", T("Portobello mushrooms", "Portobello mantarı", "Portobello-Pilze", "Champiñones portobello", "Funghi portobello", "Champignons Portobello", "波多贝罗蘑菇", "Грибы Портобелло", "فطر بورتوبيلو") },
            { "Shiitake mushrooms", T("Shiitake mushrooms", "Şitake mantarı", "Shiitake-Pilze", "Champiñones shiitake", "Funghi shiitake", "Champignons Shiitake", "香菇", "Шиитаке", "فطر شيتاكي") },
            { "Enoki mushrooms", T("Enoki mushrooms", "Enoki mantarı", "Enoki-Pilze", "Champiñones enoki", "Funghi enoki", "Champignons Enoki", "金针菇", "Еноки", "فطر اينوكي") },
            { "King oyster mushroom", T("King oyster mushroom", "Kral istiridye mantarı", "Kräuterseitling", "Seta de cardo", "Cardoncello", "Pleurote du panicaut", "杏鲍菇", "Королевская вешенка", "فطر المحار الملك") },
            { "Spinach", T("Spinach", "Ispanak", "Spinat", "Espinacas", "Spinaci", "Épinards", "菠菜", "Шпинат", "سبانخ") },
            { "Kale", T("Kale", "Kıvırcık lahana", "Grünkohl", "Col rizada", "Cavolo riccio", "Chou frisé", "羽衣甘蓝", "Кейл", "كرنب") },
            { "Swiss chard", T("Swiss chard", "Pazı", "Mangold", "Acelgas", "Bietola", "Blette", "瑞士甜菜", "Мангольд", "سلق") },
            { "Romaine lettuce", T("Romaine lettuce", "Göbek marul", "Römersalat", "Lechuga romana", "Lattuga romana", "Laitue romaine", "长叶生菜", "Ромэн", "خس روماني") },
            { "Iceberg lettuce", T("Iceberg lettuce", "Aysberg marul", "Eisbergsalat", "Lechuga iceberg", "Lattuga iceberg", "Laitue iceberg", "卷心菜", "Айсберг", "خس آيسبيرج") },
            { "Butter lettuce", T("Butter lettuce", "Tereyağlı marul", "Kopfsalat", "Lechuga de mantequilla", "Lattuga cappuccio", "Laitue beurre", "奶油生菜", "Масляный салат", "خس زبدة") },
            { "Arugula", T("Arugula", "Roka", "Rucola", "Rúcula", "Rucola", "Roquette", "芝麻菜", "Руккола", "جرجير") },
            { "Rocket", T("Rocket", "Roka", "Rauke", "Rúcula", "Rucola", "Roquette", "芝麻菜", "Руккола", "جرجير") },
            { "Watercress", T("Watercress", "Su teresi", "Brunnenkresse", "Berros", "Crescione", "Cresson", "西洋菜", "Водяной кресс", "جرجير الماء") },
            { "Mixed greens", T("Mixed greens", "Karışık yeşillikler", "Gemischte Salate", "Hojas verdes mixtas", "Insalata mista", "Salades mélangées", "混合蔬菜", "Микс зелени", "خضروات مشكلة") },
            { "Cabbage", T("Cabbage", "Lahana", "Kohl", "Col", "Cavolo", "Chou", "卷心菜", "Капуста", "ملفوف") },
            { "Green cabbage", T("Green cabbage", "Yeşil lahana", "Weißkohl", "Col verde", "Cavolo cappuccio verde", "Chou vert", "绿卷心菜", "Белокочанная капуста", "ملفوف أخضر") },
            { "Red cabbage", T("Red cabbage", "Kırmızı lahana", "Rotkohl", "Col roja", "Cavolo rosso", "Chou rouge", "红卷心菜", "Краснокочанная капуста", "ملفوف أحمر") },
            { "Savoy cabbage", T("Savoy cabbage", "Savoy lahana", "Wirsing", "Col de Milán", "Cavolo verza", "Chou de Savoie", "皱叶甘蓝", "Савойская капуста", "ملفوف سافوي") },
            { "Brussels sprouts", T("Brussels sprouts", "Brüksel lahanası", "Rosenkohl", "Coles de Bruselas", "Cavolini di Bruxelles", "Choux de Bruxelles", "抱子甘蓝", "Брюссельская капуста", "كرنب بروكسل") },
        });

        // --- Vegetables: Tubers and Pods ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Potatoes", T("Potatoes", "Patates", "Kartoffeln", "Patatas", "Patate", "Pommes de terre", "土豆", "Картофель", "بطاطا") },
            { "Russet potatoes", T("Russet potatoes", "Russet patates", "Russet-Kartoffeln", "Patatas Russet", "Patate Russet", "Pommes de terre Russet", "褐皮土豆", "Картофель Рассет", "بطاطا روسيت") },
            { "Yukon gold potatoes", T("Yukon gold potatoes", "Yukon altın patates", "Yukon Gold Kartoffeln", "Patatas Yukon Gold", "Patate Yukon Gold", "Pommes de terre Yukon Gold", "育空黄金土豆", "Картофель Юкон Голд", "بطاطا يوكون الذهبية") },
            { "Red potatoes", T("Red potatoes", "Kırmızı patates", "Rote Kartoffeln", "Patatas rojas", "Patate rosse", "Pommes de terre rouges", "红皮土豆", "Красный картофель", "بطاطا حمراء") },
            { "Sweet potatoes", T("Sweet potatoes", "Tatlı patates", "Süßkartoffeln", "Batatas", "Patate dolci", "Patates douces", "红薯", "Сладкий картофель", "بطاطا حلوة") },
            { "Yams", T("Yams", "Yer elması", "Yams", "Ñames", "Ignami", "Igname", "山药", "Ямс", "يام") },
            { "Corn", T("Corn", "Mısır", "Mais", "Maíz", "Mais", "Maïs", "玉米", "Кукуруза", "ذرة") },
            { "Baby corn", T("Baby corn", "Bebek mısır", "Babymais", "Maíz bebé", "Mais piccolo", "Maïs miniature", "迷你玉米", "Мини-кукуруза", "ذرة صغيرة") },
            { "Peas", T("Peas", "Bezelye", "Erbsen", "Guisantes", "Piselli", "Petits pois", "豌豆", "Горох", "بازلاء") },
            { "Snow peas", T("Snow peas", "Şeker bezelyesi", "Zuckerschoten", "Tirabeques", "Taccole", "Pois gourmands", "雪豆", "Снежный горошек", "بازلاء ثلجية") },
            { "Snap peas", T("Snap peas", "Çıtçıt bezelye", "Zuckererbsen", "Guisantes lágrima", "Piselli mangiatutto", "Pois croquants", "甜豆", "Сахарный горох", "بازلاء خضراء") },
            { "Green beans", T("Green beans", "Yeşil fasulye", "Grüne Bohnen", "Judías verdes", "Fagiolini", "Haricots verts", "绿豆", "Стручковая фасоль", "فاصوليا خضراء") },
            { "Yellow beans", T("Yellow beans", "Sarı fasulye", "Gelbe Bohnen", "Judías amarillas", "Fagiolini gialli", "Haricots jaunes", "黄豆", "Желтая фасоль", "فاصوليا صفراء") },
        });

        // --- Legumes and Pulses ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Lentils", T("Lentils", "Mercimek", "Linsen", "Lentejas", "Lenticchie", "Lentilles", "扁豆", "Чечевица", "عدس") },
            { "Red lentils", T("Red lentils", "Kırmızı mercimek", "Rote Linsen", "Lentejas rojas", "Lenticchie rosse", "Lentilles corail", "红扁豆", "Красная чечевица", "عدس أحمر") },
            { "Green lentils", T("Green lentils", "Yeşil mercimek", "Grüne Linsen", "Lentejas verdes", "Lenticchie verdi", "Lentilles vertes", "绿扁豆", "Зеленая чечевица", "عدس أخضر") },
            { "Brown lentils", T("Brown lentils", "Kahverengi mercimek", "Braune Linsen", "Lentejas pardas", "Lenticchie marroni", "Lentilles brunes", "棕扁豆", "Коричневая чечевица", "عدس بني") },
            { "Black lentils", T("Black lentils", "Siyah mercimek", "Schwarze Linsen", "Lentejas negras", "Lenticchie nere", "Lentilles noires", "黑扁豆", "Черная чечевица", "عدس أسود") },
            { "Split peas", T("Split peas", "Yarma bezelye", "Schälerbsen", "Guisantes partidos", "Piselli spezzati", "Pois cassés", "豌豆干", "Колотый горох", "بازلاء مقسومة") },
            { "Chickpeas", T("Chickpeas", "Nohut", "Kichererbsen", "Garbanzos", "Ceci", "Pois chiches", "鹰嘴豆", "Нут", "حمص") },
            { "White beans", T("White beans", "Beyaz fasulye", "Weiße Bohnen", "Alubias blancas", "Fagioli bianchi", "Haricots blancs", "白豆", "Белая фасоль", "فاصوليا بيضاء") },
            { "Black beans", T("Black beans", "Kara fasulye", "Schwarze Bohnen", "Frijoles negros", "Fagioli neri", "Haricots noirs", "黑豆", "Черная фасоль", "فاصوليا سوداء") },
            { "Kidney beans", T("Kidney beans", "Böbrek fasulyesi", "Kidneybohnen", "Frijoles rojos", "Fagioli rossi", "Haricots rouges", "红腰豆", "Фасоль почечная", "فاصوليا حمراء") },
            { "Fava beans", T("Fava beans", "Bakla", "Saubohnen", "Habas", "Fave", "Fèves", "蚕豆", "Бобы", "فول") },
            { "Mung beans", T("Mung beans", "Maş fasulyesi", "Mungobohnen", "Frijoles mungo", "Fagioli mung", "Haricots mungo", "绿豆", "Маш", "فول مونج") },
            { "Adzuki beans", T("Adzuki beans", "Adzuki fasulyesi", "Adzukibohnen", "Frijoles adzuki", "Fagioli azuki", "Haricots azuki", "红豆", "Адзуки", "فاصوليا أزوكية") },
            { "Pinto beans", T("Pinto beans", "Pinto fasulyesi", "Pintobohnen", "Frijoles pintos", "Fagioli pinto", "Haricots pinto", "斑豆", "Фасоль пинто", "فاصوليا بينتو") },
        });

        // --- Grains, Cereals, and Pasta ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Rice", T("Rice", "Pirinç", "Reis", "Arroz", "Riso", "Riz", "米饭", "Рис", "أرز") },
            { "Basmati rice", T("Basmati rice", "Basmati pirinci", "Basmati-Reis", "Arroz basmati", "Riso basmati", "Riz basmati", "巴斯马蒂米", "Рис басмати", "أرز بسمتي") },
            { "Jasmine rice", T("Jasmine rice", "Yasemin pirinci", "Jasminreis", "Arroz jazmín", "Riso jasmine", "Riz jasmin", "茉莉香米", "Рис жасмин", "أرز الياسمين") },
            { "Long-grain rice", T("Long-grain rice", "Uzun taneli pirinç", "Langsam-Reis", "Arroz de grano largo", "Riso a grano lungo", "Riz à grains longs", "长粒米", "Длиннозерный рис", "أرز طويل الحبة") },
            { "Short-grain rice", T("Short-grain rice", "Kısa taneli pirinç", "Kurzkorn-Reis", "Arroz de grano corto", "Riso a grano corto", "Riz à grains courts", "短粒米", "Круглозерный рис", "أرز قصير الحبة") },
            { "Sushi rice", T("Sushi rice", "Suşi pirinci", "Sushi-Reis", "Arroz para sushi", "Riso per sushi", "Riz à sushi", "寿司米", "Рис для суши", "أرز السوشي") },
            { "Arborio rice", T("Arborio rice", "Arborio pirinci", "Arborio-Reis", "Arroz Arborio", "Riso Arborio", "Riz Arborio", "阿博里奥米", "Рис Арборио", "أرز أربوريو") },
            { "Carnaroli rice", T("Carnaroli rice", "Carnaroli pirinci", "Carnaroli-Reis", "Arroz Carnaroli", "Riso Carnaroli", "Riz Carnaroli", "卡纳罗利米", "Рис Карнароли", "أرز كارنارولي") },
            { "Parboiled rice", T("Parboiled rice", "Haşlanmış pirinç", "Parboiled-Reis", "Arroz precocido", "Riso parboiled", "Riz étuvé", "半熟米", "Пропаренный рис", "أرز مسلوق") },
            { "Wild rice", T("Wild rice", "Yabani pirinç", "Wildreis", "Arroz salvaje", "Riso selvatico", "Riz sauvage", "野米", "Дикий рис", "أرز بري") },
            { "Freekeh", T("Freekeh", "Firik", "Freekeh", "Freekeh", "Freekeh", "Frikeh", "烟熏小麦", "Фрике", "فريكة") },
            { "Bulgur", T("Bulgur", "Bulgur", "Bulgur", "Bulgur", "Bulgur", "Boulgour", "布格麦", "Булгур", "برغل") },
            { "Fine bulgur", T("Fine bulgur", "İnce bulgur", "Feiner Bulgur", "Bulgur fino", "Bulgur fino", "Boulgour fin", "细布格麦", "Мелкий булгур", "برغل ناعم") },
            { "Coarse bulgur", T("Coarse bulgur", "Kalın bulgur", "Grober Bulgur", "Bulgur grueso", "Bulgur grosso", "Boulgour gros", "粗布格麦", "Крупный булгур", "برغل خشن") },
            { "Couscous", T("Couscous", "Kuskus", "Couscous", "Cuscús", "Couscous", "Couscous", "库斯库斯", "Кускус", "كسكس") },
            { "Giant couscous", T("Giant couscous", "Dev kuskus", "Riesen-Couscous", "Cuscús gigante", "Couscous gigante", "Couscous géant", "巨型库斯库斯", "Гигантский кускус", "كسكس عملاق") },
            { "Quinoa", T("Quinoa", "Kinoa", "Quinoa", "Quinua", "Quinoa", "Quinoa", "藜麦", "Киноа", "كينوا") },
            { "Buckwheat", T("Buckwheat", "Karabuğday", "Buchweizen", "Trigo sarraceno", "Grano saraceno", "Sarrasin", "荞麦", "Гречка", "حبة البركة") },
            { "Barley", T("Barley", "Arpa", "Gerste", "Cebada", "Orzo", "Orge", "大麦", "Ячмень", "شعير") },
            { "Oats", T("Oats", "Yulaf", "Hafer", "Avena", "Avena", "Avoine", "燕麦", "Овес", "شوفان") },
            { "Rolled oats", T("Rolled oats", "Yulaf ezmesi", "Haferflocken", "Avena en copos", "Fiocchi d'avena", "Flocons d'avoine", "燕麦片", "Овсяные хлопья", "شوفان ملفوف") },
            { "Steel-cut oats", T("Steel-cut oats", "Çelik kesimli yulaf", "Hafergrütze", "Avena cortada", "Avena tagliata a coltello", "Avoine coupée", "钢切燕麦", "Резаный овес", "شوفان مقطع") },
            { "Cornmeal", T("Cornmeal", "Mısır irmiği", "Maisgrieß", "Harina de maíz (gruesa)", "Farina di mais", "Farine de maïs", "玉米粉", "Кукурузная мука", "دقيق الذرة") },
            { "Polenta", T("Polenta", "Polenta", "Polenta", "Polenta", "Polenta", "Polenta", "波伦塔", "Полента", "بوليتا") },
        });

        // --- Pasta and Doughs ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Pasta", T("Pasta", "Makarna", "Nudeln", "Pasta", "Pasta", "Pâtes", "面食", "Паста", "مكرونة") },
            { "Spaghetti", T("Spaghetti", "Spagetti", "Spaghetti", "Espaguetis", "Spaghetti", "Spaghettis", "意大利面", "Спагетти", "سباغيتي") },
            { "Penne", T("Penne", "Penne", "Penne", "Penne", "Penne", "Pennes", "通心粉", "Пенне", "بيني") },
            { "Fusilli", T("Fusilli", "Fusilli", "Fusilli", "Fusilli", "Fusilli", "Fusillis", "螺旋面", "Фузилли", "فوسيلي") },
            { "Farfalle", T("Farfalle", "Farfalle", "Farfalle", "Farfalle", "Farfalle", "Farfalles", "蝴蝶面", "Фарфалле", "فارفالي") },
            { "Linguine", T("Linguine", "Linguine", "Linguine", "Linguine", "Linguine", "Linguines", "扁意粉", "Лингвини", "لينغويني") },
            { "Fettuccine", T("Fettuccine", "Fettuccine", "Fettuccine", "Fettuccine", "Fettuccine", "Fettuccines", "意大利宽面", "Феттуччине", "فيتوتشيني") },
            { "Lasagna sheets", T("Lasagna sheets", "Lazanya yaprağı", "Lasagneplatten", "Láminas de lasaña", "Sfoglie per lasagne", "Feuilles de lasagnes", "千层面皮", "Листы лазаньи", "شرائح لازانيا") },
            { "Tagliatelle", T("Tagliatelle", "Tagliatelle", "Tagliatelle", "Tallarines", "Tagliatelle", "Tagliatelles", "意大利细面", "Тальятелле", "تاغلياتيلي") },
            { "Macaroni", T("Macaroni", "Makarna", "Makkaroni", "Macarrones", "Maccheroni", "Macaronis", "通心粉", "Макароны", "معكرونة") },
            { "Ravioli sheets", T("Ravioli sheets", "Ravioli hamuru", "Ravioli-Blätter", "Láminas de raviolis", "Sfoglie per ravioli", "Pâtes à raviolis", "馄饨皮", "Листы для равиоли", "عجينة رافيولي") },
            { "Gnocchi", T("Gnocchi", "Gnocchi", "Gnocchi", "Gnocchi", "Gnocchi", "Gnocchis", "土豆丸子", "Ньокки", "نيوكي") },
            { "Pizza dough", T("Pizza dough", "Pizza hamuru", "Pizzateig", "Masa de pizza", "Impasto per pizza", "Pâte à pizza", "披萨面团", "Тесто для пиццы", "عجينة البيتزا") },
            { "Pide dough", T("Pide dough", "Pide hamuru", "Pide-Teig", "Masa de pide", "Impasto per pide", "Pâte à pide", "皮德面团", "Тесто для пиде", "عجينة البيدا") },
            { "Lahmacun dough", T("Lahmacun dough", "Lahmacun hamuru", "Lahmacun-Teig", "Masa de lahmacun", "Impasto per lahmacun", "Pâte à lahmacun", "土耳其披萨面团", "Тесто для лахмаджуна", "عجينة لحم العجين") },
            { "Yufka", T("Yufka", "Yufka", "Yufka-Teig", "Masa yufka", "Yufka (pasta)", "Pâte yufka", "土耳其薄面皮", "Юфка", "عجينة اليوفكا") },
            { "Phyllo dough", T("Phyllo dough", "Milföy hamuru", "Filoteig", "Masa filo", "Pasta fillo", "Pâte filo", "千层酥皮", "Тесто фило", "عجينة فيلو") },
            { "Pita bread", T("Pita bread", "Pide ekmeği", "Pita-Brot", "Pan de pita", "Pane pita", "Pain pita", "皮塔饼", "Пита", "خبز البيتا") },
            { "Saj bread", T("Saj bread", "Sac ekmeği", "Saj-Brot", "Pan saj", "Pane saj", "Pain saj", "萨杰面包", "Хлеб садж", "خبز الصاج") },
            { "Naan", T("Naan", "Naan", "Naan", "Naan", "Naan", "Naan", "馕", "Наан", "خبز النان") },
            { "Lavash", T("Lavash", "Lavaş", "Lavash", "Pan lavash", "Lavash", "Lavash", "拉瓦什", "Лаваш", "خبز اللافا") },
            { "Simit dough", T("Simit dough", "Simit hamuru", "Simit-Teig", "Masa de simit", "Impasto per simit", "Pâte à simit", "土耳其芝麻圈面团", "Тесто для симита", "عجينة السميت") },
            { "Gözleme dough", T("Gözleme dough", "Gözleme hamuru", "Gözleme-Teig", "Masa de gözleme", "Impasto per gözleme", "Pâte à gözleme", "土耳其薄饼面团", "Тесто для гёзлеме", "عجينة الكوزلمي") },
            { "Bazlama", T("Bazlama", "Bazlama", "Bazlama", "Bazlama", "Bazlama", "Bazlama", "巴兹拉玛", "Базлама", "بازلاما") },
            { "Tortilla", T("Tortilla", "Tortilla", "Tortilla", "Tortilla", "Tortilla", "Tortilla", "墨西哥薄饼", "Тортилья", "تورتيلا") },
            { "Baguette", T("Baguette", "Baget", "Baguette", "Baguette", "Baguette", "Baguette", "法棍面包", "Багет", "باغيت") },
            { "Ciabatta", T("Ciabatta", "Ciabatta", "Ciabatta", "Ciabatta", "Ciabatta", "Ciabatta", "夏巴塔面包", "Чиабатта", "شياباتا") },
            { "Sourdough bread", T("Sourdough bread", "Ekşi mayalı ekmek", "Sauerteigbrot", "Pan de masa madre", "Pane a lievitazione naturale", "Pain au levain", "酸面包", "Хлеб на закваске", "خبز الخميرة الطبيعية") },
            { "Brioche", T("Brioche", "Briyoş", "Brioche", "Brioche", "Brioche", "Brioche", "布里欧修面包", "Бриошь", "بريوش") },
            { "Burger buns", T("Burger buns", "Hamburger ekmeği", "Burgerbrötchen", "Pan de hamburguesa", "Panini per hamburger", "Pains à burger", "汉堡面包", "Булочки для бургеров", "خبز البرجر") },
            { "Hot dog buns", T("Hot dog buns", "Sosisli sandviç ekmeği", "Hot-Dog-Brötchen", "Pan para perrito caliente", "Panini per hot dog", "Pains à hot-dog", "热狗面包", "Булочки для хот-догов", "خبز الهوت دوغ") },
            { "Wrap bread", T("Wrap bread", "Dürüm ekmeği", "Wrap-Brot", "Pan para wrap", "Pane per wrap", "Pain à wrap", "卷饼面包", "Лаваш для роллов", "خبز اللفائف") },
            { "Manakish dough", T("Manakish dough", "Manakish hamuru", "Manakish-Teig", "Masa de manakish", "Impasto per manakish", "Pâte à Manakish", "马纳基什面团", "Тесто для манакиш", "عجينة مناقيش") },
            { "Fatayer dough", T("Fatayer dough", "Fatayer hamuru", "Fatayer-Teig", "Masa de fatayer", "Impasto per fatayer", "Pâte à Fatayer", "法塔耶面团", "Тесто для фатаер", "عجينة فطاير") },
            { "Börek dough", T("Börek dough", "Börek hamuru", "Börek-Teig", "Masa de börek", "Impasto per börek", "Pâte à Börek", "土耳其馅饼面团", "Тесто для бёрека", "عجينة بوريك") },
        });

        // --- Dairy: Milk, Cream, and Cultured Products ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Milk", T("Milk", "Süt", "Milch", "Leche", "Latte", "Lait", "牛奶", "Молоко", "حليب") },
            { "Whole milk", T("Whole milk", "Tam yağlı süt", "Vollmilch", "Leche entera", "Latte intero", "Lait entier", "全脂牛奶", "Цельное молоко", "حليب كامل الدسم") },
            { "Skim milk", T("Skim milk", "Yağsız süt", "Magermilch", "Leche desnatada", "Latte scremato", "Lait écrémé", "脱脂牛奶", "Обезжиренное молоко", "حليب خالي الدسم") },
            { "Low-fat milk", T("Low-fat milk", "Az yağlı süt", "Fettarme Milch", "Leche semidesnatada", "Latte parzialmente scremato", "Lait demi-écrémé", "低脂牛奶", "Молоко с низким содержанием жира", "حليب قليل الدسم") },
            { "Goat milk", T("Goat milk", "Keçi sütü", "Ziegenmilch", "Leche de cabra", "Latte di capra", "Lait de chèvre", "山羊奶", "Козье молоко", "حليب الماعز") },
            { "Sheep milk", T("Sheep milk", "Koyun sütü", "Schafmilch", "Leche de oveja", "Latte di pecora", "Lait de brebis", "羊奶", "Овечье молоко", "حليب الأغنام") },
            { "Cream", T("Cream", "Krema", "Sahne", "Crema", "Panna", "Crème", "奶油", "Сливки", "قشدة") },
            { "Heavy cream", T("Heavy cream", "Yoğun krema", "Schlagsahne", "Crema espesa", "Panna da montare", "Crème épaisse", "重奶油", "Густые сливки", "كريمة ثقيلة") },
            { "Light cream", T("Light cream", "Hafif krema", "Leichte Sahne", "Nata ligera", "Panna leggera", "Crème légère", "淡奶油", "Легкие сливки", "كريمة خفيفة") },
            { "Sour cream", T("Sour cream", "Ekşi krema", "Sauerrahm", "Crema agria", "Panna acida", "Crème aigre", "酸奶油", "Сметана", "كريمة حامضة") },
            { "Crème fraîche", T("Crème fraîche", "Krem şanti", "Crème fraîche", "Crema fresca", "Crème fraîche", "Crème fraîche", "法式鲜奶油", "Крем-фреш", "كريمة طازجة") },
            { "Yogurt", T("Yogurt", "Yoğurt", "Joghurt", "Yogur", "Yogurt", "Yaourt", "酸奶", "Йогурт", "زبادي") },
            { "Greek yogurt", T("Greek yogurt", "Süzme yoğurt", "Griechischer Joghurt", "Yogur griego", "Yogurt greco", "Yaourt grec", "希腊酸奶", "Греческий йогурт", "زبادي يوناني") },
            { "Strained yogurt", T("Strained yogurt", "Süzme yoğurt", "Abgetropfter Joghurt", "Yogur colado", "Yogurt colato", "Yaourt égoutté", "滤干酸奶", "Процеженный йогурт", "زبادي مصفى") },
            { "Labneh", T("Labneh", "Labne", "Labneh", "Labneh", "Labneh", "Labneh", "拉布内", "Лабне", "لبنة") },
            { "Kaymak", T("Kaymak", "Kaymak", "Kaymak", "Kaymak", "Kaymak", "Kaymak", "卡伊马克", "Каймак", "قيمر") },
        });

        // --- Dairy: Cheeses ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Cream cheese", T("Cream cheese", "Krem peynir", "Frischkäse", "Queso crema", "Formaggio cremoso", "Fromage frais", "奶油芝士", "Сливочный сыр", "جبن كريمي") },
            { "Mascarpone", T("Mascarpone", "Mascarpone", "Mascarpone", "Mascarpone", "Mascarpone", "Mascarpone", "马斯卡彭", "Маскарпоне", "جبن المسكربون") },
            { "Ricotta", T("Ricotta", "Ricotta", "Ricotta", "Ricotta", "Ricotta", "Ricotta", "意大利乳清干酪", "Рикотта", "جبن الريكوتا") },
            { "Mozzarella", T("Mozzarella", "Mozzarella", "Mozzarella", "Mozzarella", "Mozzarella", "Mozzarella", "马苏里拉奶酪", "Моцарелла", "جبن الموزاريلا") },
            { "Fresh mozzarella", T("Fresh mozzarella", "Taze mozzarella", "Frische Mozzarella", "Mozzarella fresca", "Mozzarella fresca", "Mozzarella fraîche", "新鲜马苏里拉", "Свежая моцарелла", "موزاريلا طازجة") },
            { "Buffalo mozzarella", T("Buffalo mozzarella", "Manda mozzarella", "Büffel-Mozzarella", "Mozzarella de búfala", "Mozzarella di bufala", "Mozzarella de bufflonne", "水牛奶酪", "Моцарелла из буйволиного молока", "موزاريلا الجاموس") },
            { "Cheddar", T("Cheddar", "Cheddar", "Cheddar", "Cheddar", "Cheddar", "Cheddar", "切达干酪", "Чеддер", "جبن الشيدر") },
            { "White cheddar", T("White cheddar", "Beyaz cheddar", "Weißer Cheddar", "Cheddar blanco", "Cheddar bianco", "Cheddar blanc", "白切达干酪", "Белый чеддер", "شيدر أبيض") },
            { "Yellow cheddar", T("Yellow cheddar", "Sarı cheddar", "Gelber Cheddar", "Cheddar amarillo", "Cheddar giallo", "Cheddar jaune", "黄切达干酪", "Желтый чеддер", "شيدر أصفر") },
            { "Parmesan", T("Parmesan", "Parmesan", "Parmesan", "Parmesano", "Parmigiano", "Parmesan", "帕尔马干酪", "Пармезан", "جبن البارميزان") },
            { "Grana padano", T("Grana padano", "Grana Padano", "Grana Padano", "Grana Padano", "Grana Padano", "Grana Padano", "帕达诺干酪", "Грана Падано", "جرانا بادانو") },
            { "Pecorino romano", T("Pecorino romano", "Pecorino Romano", "Pecorino Romano", "Pecorino romano", "Pecorino romano", "Pecorino romano", "佩科里诺罗马诺", "Пекорино Романо", "جبن بيكورينو رومانو") },
            { "Gorgonzola", T("Gorgonzola", "Gorgonzola", "Gorgonzola", "Gorgonzola", "Gorgonzola", "Gorgonzola", "戈贡佐拉", "Горгонзола", "جبن الغورغونزولا") },
            { "Blue cheese", T("Blue cheese", "Mavi peynir", "Blauschimmelkäse", "Queso azul", "Formaggio erborinato", "Fromage bleu", "蓝纹奶酪", "Голубой сыр", "جبن أزرق") },
            { "Brie", T("Brie", "Brie", "Brie", "Brie", "Brie", "Brie", "布里干酪", "Бри", "جبن بري") },
            { "Camembert", T("Camembert", "Camembert", "Camembert", "Camembert", "Camembert", "Camembert", "卡门贝尔", "Камамбер", "جبن كامembert") },
            { "Edam", T("Edam", "Edam", "Edamer", "Edam", "Edam", "Edam", "伊丹干酪", "Эдам", "جبن إيدام") },
            { "Gouda", T("Gouda", "Gouda", "Gouda", "Gouda", "Gouda", "Gouda", "豪达干酪", "Гауда", "جبن جودة") },
            { "Smoked gouda", T("Smoked gouda", "İsli gouda", "Geräucherter Gouda", "Gouda ahumado", "Gouda affumicato", "Gouda fumé", "烟熏豪达干酪", "Копченая гауда", "جبن جودة مدخن") },
            { "Gruyere", T("Gruyere", "Gruyere", "Greyerzer", "Gruyer", "Gruviera", "Gruyère", "格鲁耶尔干酪", "Грюйер", "جبن غرويير") },
            { "Emmental", T("Emmental", "Emmental", "Emmentaler", "Emmental", "Emmental", "Emmental", "艾门塔尔干酪", "Эмменталь", "جبن إيمنتال") },
            { "Swiss cheese", T("Swiss cheese", "İsviçre peyniri", "Schweizer Käse", "Queso suizo", "Formaggio svizzero", "Fromage suisse", "瑞士奶酪", "Швейцарский сыр", "جبن سويسري") },
            { "Halloumi", T("Halloumi", "Hellim", "Halloumi", "Halloumi", "Halloumi", "Halloumi", "哈罗米干酪", "Халлуми", "جبن الحلوم") },
            { "Feta", T("Feta", "Feta", "Feta", "Feta", "Feta", "Feta", "羊乳酪", "Фета", "جبن فيتا") },
            { "Akkawi cheese", T("Akkawi cheese", "Akkawi peyniri", "Akkawi-Käse", "Queso akkawi", "Formaggio Akkawi", "Fromage Akkawi", "阿卡维奶酪", "Аккави", "جبن عكاوي") },
            { "Nabulsi cheese", T("Nabulsi cheese", "Nabulsi peyniri", "Nabulsi-Käse", "Queso Nabulsi", "Formaggio Nabulsi", "Fromage Nabulsi", "纳布尔西奶酪", "Набулси", "جبن نابلسي") },
            { "Tulum cheese", T("Tulum cheese", "Tulum peyniri", "Tulum-Käse", "Queso Tulum", "Formaggio Tulum", "Fromage Tulum", "图卢姆奶酪", "Тулум", "جبن تولوم") },
            { "Kashkaval", T("Kashkaval", "Kaşkaval", "Kashkaval", "Kashkaval", "Caciocavallo", "Kashkaval", "卡什卡瓦尔", "Кашкавал", "جبن القشقوان") },
            { "Kaşar cheese", T("Kaşar cheese", "Kaşar peyniri", "Kaşar-Käse", "Queso Kaşar", "Formaggio Kaşar", "Fromage Kaşar", "卡萨奶酪", "Кашар", "جبن القشار") },
            { "Provolone", T("Provolone", "Provolone", "Provolone", "Provolone", "Provolone", "Provolone", "波萝伏洛干酪", "Проволоне", "جبن بروفولون") },
            { "Asiago", T("Asiago", "Asiago", "Asiago", "Asiago", "Asiago", "Asiago", "阿夏戈干酪", "Азиаго", "جبن آسياغو") },
            { "Burrata", T("Burrata", "Burrata", "Burrata", "Burrata", "Burrata", "Burrata", "布拉塔干酪", "Буррата", "جبن البوراتا") },
        });

        // --- Meats: Beef and Veal ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Beef", T("Beef", "Dana eti", "Rindfleisch", "Carne de res", "Carne di manzo", "Bœuf", "牛肉", "Говядина", "لحم بقري") },
            { "Ground beef", T("Ground beef", "Kıyma", "Rinderhackfleisch", "Carne molida de res", "Macinato di manzo", "Bœuf haché", "牛肉末", "Говяжий фарш", "لحم بقري مفروم") },
            { "Sirloin", T("Sirloin", "Sığır filetosu", "Filet", "Solomillo", "Controfiletto", "Aloyau", "牛腩", "Филе", "فيليه") },
            { "Ribeye", T("Ribeye", "Antrikot", "Ribeye-Steak", "Chuletón", "Costata", "Entrecôte", "肉眼牛排", "Рибай", "شريحة لحم الضلع") },
            { "Tenderloin", T("Tenderloin", "Bonfile", "Filet", "Lomo", "Filetto", "Filet", "里脊肉", "Вырезка", "لحم المتن") },
            { "Short ribs", T("Short ribs", "Kısa kaburga", "Kurzrippen", "Costillas cortas", "Costine", "Côtes courtes", "牛小排", "Короткие ребрышки", "أضلاع قصيرة") },
            { "Brisket", T("Brisket", "Döş eti", "Rinderbrust", "Pechuga de res", "Punta di petto", "Poitrine de bœuf", "牛胸肉", "Грудинка", "لحم الصدر") },
            { "Beef cubes", T("Beef cubes", "Kuşbaşı et", "Rindfleischwürfel", "Cubos de carne de res", "Cubetti di manzo", "Dés de bœuf", "牛肉块", "Кубики говядины", "مكعبات لحم بقري") },
            { "Veal", T("Veal", "Buzağı eti", "Kalbfleisch", "Ternera", "Vitello", "Veau", "小牛肉", "Телятина", "لحم عجل") },
            { "Veal cutlets", T("Veal cutlets", "Dana pirzola", "Kalbsschnitzel", "Chuletas de ternera", "Cotolette di vitello", "Escalopes de veau", "小牛排", "Телячьи отбивные", "شرائح لحم العجل") },
        });

        // --- Meats: Lamb and Goat ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Lamb", T("Lamb", "Kuzu eti", "Lammfleisch", "Cordero", "Agnello", "Agneau", "羊肉", "Баранина", "لحم الضأن") },
            { "Lamb chops", T("Lamb chops", "Kuzu pirzola", "Lammkoteletts", "Chuletas de cordero", "Costolette d'agnello", "Côtelettes d'agneau", "羊排", "Бараньи отбивные", "ريش الضأن") },
            { "Lamb shoulder", T("Lamb shoulder", "Kuzu kol", "Lammschulter", "Paletilla de cordero", "Spalla d'agnello", "Épaule d'agneau", "羊肩肉", "Лопатка ягненка", "كتف الضأن") },
            { "Lamb shank", T("Lamb shank", "Kuzu incik", "Lammhaxe", "Pierna de cordero", "Stinco d'agnello", "Jarret d'agneau", "羊腿", "Голяшка ягненка", "ساق الضأن") },
            { "Ground lamb", T("Ground lamb", "Kuzu kıyması", "Lammhackfleisch", "Carne molida de cordero", "Macinato d'agnello", "Agneau haché", "羊肉末", "Бараний фарш", "لحم ضأن مفروم") },
            { "Lamb liver", T("Lamb liver", "Kuzu ciğeri", "Lammleber", "Hígado de cordero", "Fegato d'agnello", "Foie d'agneau", "羊肝", "Баранья печень", "كبد الضأن") },
            { "Goat meat", T("Goat meat", "Keçi eti", "Ziegenfleisch", "Carne de cabra", "Carne di capra", "Viande de chèvre", "山羊肉", "Козлятина", "لحم الماعز") },
            { "Goat liver", T("Goat liver", "Keçi ciğeri", "Ziegenleber", "Hígado de cabra", "Fegato di capra", "Foie de chèvre", "山羊肝", "Козья печень", "كبد الماعز") },
        });

        // --- Meats: Poultry and Small Game ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Chicken breast", T("Chicken breast", "Tavuk göğsü", "Hähnchenbrust", "Pechuga de pollo", "Petto di pollo", "Blanc de poulet", "鸡胸肉", "Куриная грудка", "صدر دجاج") },
            { "Chicken thigh", T("Chicken thigh", "Tavuk budu", "Hähnchenschenkel", "Muslo de pollo", "Coscia di pollo", "Cuisse de poulet", "鸡腿肉", "Куриное бедро", "فخذ دجاج") },
            { "Chicken wings", T("Chicken wings", "Tavuk kanadı", "Hähnchenflügel", "Alas de pollo", "Ali di pollo", "Ailes de poulet", "鸡翅", "Куриные крылья", "أجنحة دجاج") },
            { "Whole chicken", T("Whole chicken", "Bütün tavuk", "Ganzes Hähnchen", "Pollo entero", "Pollo intero", "Poulet entier", "全鸡", "Целая курица", "دجاجة كاملة") },
            { "Chicken drumsticks", T("Chicken drumsticks", "Tavuk baget", "Hähnchenkeulen", "Contramuslos de pollo", "Fusi di pollo", "Pilons de poulet", "鸡小腿", "Куриные ножки", "سيقان الدجاج") },
            { "Ground chicken", T("Ground chicken", "Tavuk kıyması", "Hähnchenhackfleisch", "Carne molida de pollo", "Macinato di pollo", "Poulet haché", "鸡肉末", "Куриный фарш", "لحم دجاج مفروم") },
            { "Turkey breast", T("Turkey breast", "Hindi göğsü", "Putenbrust", "Pechuga de pavo", "Petto di tacchino", "Blanc de dinde", "火鸡胸肉", "Грудка индейки", "صدر ديك رومي") },
            { "Turkey thigh", T("Turkey thigh", "Hindi budu", "Putenschenkel", "Muslo de pavo", "Coscia di tacchino", "Cuisse de dinde", "火鸡腿", "Бедро индейки", "فخذ ديك رومي") },
            { "Ground turkey", T("Ground turkey", "Hindi kıyması", "Putenhackfleisch", "Carne molida de pavo", "Macinato di tacchino", "Dinde hachée", "火鸡肉末", "Фарш индейки", "لحم ديك رومي مفروم") },
            { "Duck breast", T("Duck breast", "Ördek göğsü", "Entenbrust", "Pechuga de pato", "Petto d'anatra", "Magret de canard", "鸭胸肉", "Утиная грудка", "صدر بط") },
            { "Duck leg", T("Duck leg", "Ördek budu", "Entenkeule", "Muslo de pato", "Coscia d'anatra", "Cuisse de canard", "鸭腿", "Утиная ножка", "فخذ بط") },
            { "Rabbit", T("Rabbit", "Tavşan", "Kaninchen", "Conejo", "Coniglio", "Lapin", "兔肉", "Кролик", "أرنب") },
            { "Quail", T("Quail", "Bıldırcın", "Wachtel", "Codorniz", "Quaglia", "Caille", "鹌鹑", "Перепел", "سمان") },
            { "Pigeon meat", T("Pigeon meat", "Güvercin eti", "Taubenfleisch", "Carne de paloma", "Carne di piccione", "Viande de pigeon", "鸽肉", "Голубиное мясо", "لحم الحمام") },
        });

        // --- Meats: Cured and Processed ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Sucuk", T("Sucuk", "Sucuk", "Sucuk", "Sucuk", "Sucuk", "Sucuk", "土耳其香肠", "Суджук", "سجق") },
            { "Pastirma", T("Pastirma", "Pastırma", "Pastırma", "Pastirma", "Pastirma", "Pastirma", "风干牛肉", "Пастырма", "بسطرمة") },
            { "Kobas", T("Kobas", "Kobas", "Kobas", "Kobas", "Kobas", "Kobas", "科巴斯香肠", "Кобас", "كوباس") },
            { "Sausage", T("Sausage", "Sosis", "Wurst", "Salchicha", "Salsiccia", "Saucisse", "香肠", "Колбаса", "نقانق") },
            { "Beef sausage", T("Beef sausage", "Dana sosis", "Rindswurst", "Salchicha de res", "Salsiccia di manzo", "Saucisse de bœuf", "牛肉香肠", "Говяжья колбаса", "نقانق لحم بقري") },
            { "Turkey sausage", T("Turkey sausage", "Hindi sosis", "Putensalami", "Salchicha de pavo", "Salsiccia di tacchino", "Saucisse de dinde", "火鸡香肠", "Колбаса из индейки", "نقانق ديك رومي") },
            { "Pepperoni", T("Pepperoni", "Pepperoni", "Pepperoni", "Pepperoni", "Pepperoni", "Pepperoni", "意大利辣香肠", "Пепперони", "بيبروني") },
            { "Salami", T("Salami", "Salam", "Salami", "Salami", "Salame", "Salami", "萨拉米", "Салями", "سلامي") },
            { "Turkey salami", T("Turkey salami", "Hindi salam", "Putensalami", "Salami de pavo", "Salame di tacchino", "Salami de dinde", "火鸡萨拉米", "Салями из индейки", "سلامي ديك رومي") },
            { "Mortadella", T("Mortadella", "Mortadella", "Mortadella", "Mortadela", "Mortadella", "Mortadelle", "意大利肉肠", "Мортаделла", "مرتديلا") },
            { "Pastrami", T("Pastrami", "Pastrami", "Pastrami", "Pastrami", "Pastrami", "Pastrami", "五香熏牛肉", "Пастрами", "باسترامي") },
            { "Meatballs", T("Meatballs", "Köfte", "Fleischbällchen", "Albóndigas", "Polpette", "Boulettes de viande", "肉丸", "Фрикадельки", "كرات اللحم") },
            { "Kofte mix", T("Kofte mix", "Köfte harcı", "Frikadellen-Mischung", "Mezcla para albóndigas", "Impasto per polpette", "Mélange à boulettes", "肉丸混合物", "Смесь для кюфты", "خليط كفتة") },
            { "Shawarma chicken", T("Shawarma chicken", "Tavuk döner", "Hähnchen-Döner", "Pollo para shawarma", "Pollo per shawarma", "Poulet chawarma", "鸡肉沙威玛", "Куриная шаурма", "دجاج الشاورما") },
            { "Shawarma beef", T("Shawarma beef", "Dana döner", "Rindfleisch-Döner", "Carne de res para shawarma", "Manzo per shawarma", "Bœuf chawarma", "牛肉沙威玛", "Говяжья шаурма", "لحم شاورما") },
            { "Doner meat", T("Doner meat", "Döner eti", "Dönerfleisch", "Carne para döner", "Carne per döner", "Viande à kebab", "烤肉串肉", "Мясо для донера", "لحم الدونر") },
        });

        // --- Seafood: Fish ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Fish", T("Fish", "Balık", "Fisch", "Pescado", "Pesce", "Poisson", "鱼", "Рыба", "سمك") },
            { "Salmon", T("Salmon", "Somon", "Lachs", "Salmón", "Salmone", "Saumon", "三文鱼", "Лосось", "سلمون") },
            { "Tuna", T("Tuna", "Ton balığı", "Thunfisch", "Atún", "Tonno", "Thon", "金枪鱼", "Тунец", "تونا") },
            { "Sardines", T("Sardines", "Sardalya", "Sardinen", "Sardinas", "Sardine", "Sardines", "沙丁鱼", "Сардины", "سردين") },
            { "Anchovies", T("Anchovies", "Hamsi", "Sardellen", "Anchoas", "Acciughe", "Anchois", "凤尾鱼", "Анчоусы", "أنشوجة") },
            { "Mackerel", T("Mackerel", "Uskumru", "Makrele", "Caballa", "Sgombro", "Maquereau", "鲭鱼", "Скумбрия", "ماكريل") },
            { "Sea bass", T("Sea bass", "Levrek", "Seebarsch", "Lubina", "Spigola", "Bar", "鲈鱼", "Морской окунь", "قاروص بحري") },
            { "Sea bream", T("Sea bream", "Çipura", "Dorade", "Dorada", "Orata", "Daurade", "鲷鱼", "Морской лещ", "دنيس") },
            { "Tilapia", T("Tilapia", "Tilapia", "Tilapia", "Tilapia", "Tilapia", "Tilapia", "罗非鱼", "Тилапия", "بلطي") },
            { "Cod", T("Cod", "Morina", "Kabeljau", "Bacalao", "Merluzzo", "Morue", "鳕鱼", "Треска", "قد") },
            { "Haddock", T("Haddock", "Mezgit", "Schellfisch", "Eglefino", "Eglefino", "Aiglefin", "黑线鳕", "Пикша", "حدوق") },
        });

        // --- Seafood: Shellfish and Mollusks ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Shrimp", T("Shrimp", "Karides", "Garnelen", "Camarones", "Gamberetti", "Crevettes", "虾", "Креветки", "جمبري") },
            { "Prawns", T("Prawns", "Karides", "Garnelen", "Langostinos", "Gamberi", "Grosses crevettes", "大虾", "Крупные креветки", "قريدس") },
            { "Crab", T("Crab", "Yengeç", "Krabbe", "Cangrejo", "Granchio", "Crabe", "螃蟹", "Краб", "سرطان البحر") },
            { "Lobster", T("Lobster", "Istakoz", "Hummer", "Langosta", "Aragosta", "Homard", "龙虾", "Лобстер", "كابوريا") },
            { "Squid", T("Squid", "Kalamar", "Tintenfisch", "Calamar", "Calamaro", "Calmar", "鱿鱼", "Кальмар", "حبار") },
            { "Octopus", T("Octopus", "Ahtapot", "Krake", "Pulpo", "Polpo", "Poulpe", "章鱼", "Осьминог", "أخطبوط") },
            { "Clams", T("Clams", "İstiridye", "Muscheln (Venusmuscheln)", "Almejas", "Vongole", "Palourdes", "蛤蜊", "Моллюски", "محار") },
            { "Mussels", T("Mussels", "Midye", "Miesmuscheln", "Mejillones", "Cozze", "Moules", "贻贝", "Мидии", "بلح البحر") },
            { "Oysters", T("Oysters", "İstiridye", "Austern", "Ostras", "Ostriche", "Huîtres", "牡蛎", "Устрицы", "محار") },
            { "Scallops", T("Scallops", "Tarak", "Jakobsmuscheln", "Vieiras", "Capesante", "Noix de Saint-Jacques", "扇贝", "Морские гребешки", "أسقلوب") },
        });

        // --- Eggs and Plant-Based Proteins ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Eggs", T("Eggs", "Yumurta", "Eier", "Huevos", "Uova", "Œufs", "鸡蛋", "Яйца", "بيض") },
            { "Quail eggs", T("Quail eggs", "Bıldırcın yumurtası", "Wachteleier", "Huevos de codorniz", "Uova di quaglia", "Œufs de caille", "鹌鹑蛋", "Перепелиные яйца", "بيض السمان") },
            { "Duck eggs", T("Duck eggs", "Ördek yumurtası", "Enteneier", "Huevos de pato", "Uova d'anatra", "Œufs de canard", "鸭蛋", "Утиные яйца", "بيض البط") },
            { "Tofu", T("Tofu", "Tofu", "Tofu", "Tofu", "Tofu", "Tofu", "豆腐", "Тофу", "توفو") },
            { "Tempeh", T("Tempeh", "Tempeh", "Tempeh", "Tempeh", "Tempeh", "Tempeh", "天贝", "Темпе", "تمبيه") },
            { "Seitan", T("Seitan", "Seitan", "Seitan", "Seitán", "Seitan", "Seitan", "面筋", "Сейтан", "سيتان") },
            { "Soybeans", T("Soybeans", "Soya fasulyesi", "Sojabohnen", "Habas de soja", "Fagioli di soia", "Graines de soja", "大豆", "Соевые бобы", "فول الصويا") },
            { "Edamame", T("Edamame", "Edamame", "Edamame", "Edamame", "Edamame", "Edamame", "毛豆", "Эдамаме", "إدامامي") },
        });

        // --- Asian Sauces and Pastes ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Miso", T("Miso", "Miso", "Miso", "Miso", "Miso", "Miso", "味噌", "Мисо", "ميسو") },
            { "Soy sauce", T("Soy sauce", "Soya sosu", "Sojasauce", "Salsa de soja", "Salsa di soia", "Sauce soja", "酱油", "Соевый соус", "صلصة الصويا") },
            { "Tamari", T("Tamari", "Tamari", "Tamari", "Tamari", "Tamari", "Tamari", "溜酱油", "Тамари", "تاماري") },
            { "Fish sauce", T("Fish sauce", "Balık sosu", "Fischsauce", "Salsa de pescado", "Salsa di pesce", "Sauce de poisson", "鱼露", "Рыбный соус", "صلصة السمك") },
            { "Oyster sauce", T("Oyster sauce", "İstiridye sosu", "Austernsauce", "Salsa de ostras", "Salsa d'ostriche", "Sauce d'huîtres", "蚝油", "Устричный соус", "صلصة المحار") },
            { "Hoisin sauce", T("Hoisin sauce", "Hoisin sosu", "Hoisin-Sauce", "Salsa hoisin", "Salsa hoisin", "Sauce hoisin", "海鲜酱", "Соус хойсин", "صلصة هويسين") },
            { "Teriyaki sauce", T("Teriyaki sauce", "Teriyaki sosu", "Teriyaki-Sauce", "Salsa teriyaki", "Salsa teriyaki", "Sauce teriyaki", "照烧酱", "Соус терияки", "صلصة ترياكي") },
            { "Kimchi", T("Kimchi", "Kimçi", "Kimchi", "Kimchi", "Kimchi", "Kimchi", "泡菜", "Кимчи", "كيمتشي") },
            { "Gochujang", T("Gochujang", "Gochujang", "Gochujang", "Gochujang", "Gochujang", "Gochujang", "韩式辣椒酱", "Кочуджан", "غوتشوجانغ") },
        });

        // --- Dips, Spreads, and Nut Butters ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Tahini", T("Tahini", "Tahin", "Tahini", "Tahini", "Tahina", "Tahini", "芝麻酱", "Тахини", "طحينة") },
            { "Hummus", T("Hummus", "Humus", "Hummus", "Hummus", "Hummus", "Houmous", "鹰嘴豆泥", "Хумус", "حمص") },
            { "Baba ghanoush", T("Baba ghanoush", "Baba gannuş", "Baba Ghanoush", "Baba Ghanoush", "Baba Ghanoush", "Caviar d'aubergine", "烤茄泥", "Баба гануш", "بابا غنوج") },
            { "Moutabal", T("Moutabal", "Mütebbel", "Mutabal", "Mutabal", "Mutabal", "Moutabal", "茄子泥", "Мутабаль", "متبل") },
            { "Labneh balls", T("Labneh balls", "Top labne", "Labneh-Kugeln", "Bolas de labneh", "Palline di labneh", "Balles de labneh", "球状 لبنة", "Шарики лабне", "كرات اللبنة") },
            { "Nutella", T("Nutella", "Nutella", "Nutella", "Nutella", "Nutella", "Nutella", "能多益", "Нутелла", "نوتيلا") },
            { "Peanut butter", T("Peanut butter", "Fıstık ezmesi", "Erdnussbutter", "Mantequilla de cacahuete", "Burro di arachidi", "Beurre de cacahuète", "花生酱", "Арахисовое масло", "زبدة الفول السوداني") },
            { "Almond butter", T("Almond butter", "Badem ezmesi", "Mandelbutter", "Mantequilla de almendras", "Burro di mandorle", "Beurre d'amande", "杏仁酱", "Миндальное масло", "زبدة اللوز") },
            { "Cashew butter", T("Cashew butter", "Kaju ezmesi", "Cashewbutter", "Mantequilla de anacardo", "Burro di anacardi", "Beurre de cajou", "腰果酱", "Кешью масло", "زبدة الكاجو") },
            { "Pistachio cream", T("Pistachio cream", "Fıstık kreması", "Pistaziencreme", "Crema de pistacho", "Crema di pistacchio", "Crème de pistache", "开心果酱", "Фисташковый крем", "كريمة الفستق") },
        });

        // --- Nuts and Seeds ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Walnuts", T("Walnuts", "Ceviz", "Walnüsse", "Nueces", "Noci", "Noix", "核桃", "Грецкие орехи", "جوز") },
            { "Hazelnuts", T("Hazelnuts", "Fındık", "Haselnüsse", "Avellanas", "Nocciole", "Noisettes", "榛子", "Фундук", "بندق") },
            { "Almonds", T("Almonds", "Badem", "Mandeln", "Almendras", "Mandorle", "Amandes", "杏仁", "Миндаль", "لوز") },
            { "Cashews", T("Cashews", "Kaju", "Cashewnüsse", "Anacardos", "Anacardi", "Noix de cajou", "腰果", "Кешью", "كاجو") },
            { "Macadamia nuts", T("Macadamia nuts", "Makadami fındığı", "Macadamia-Nüsse", "Nueces de macadamia", "Noci di macadamia", "Noix de macadamia", "澳洲坚果", "Макадамия", "مكاداميا") },
            { "Pecans", T("Pecans", "Pekan cevizi", "Pekannüsse", "Pacanas", "Noci pecan", "Noix de pécan", "山核桃", "Пекан", "بيكان") },
            { "Pine nuts", T("Pine nuts", "Dolmalık fıstık", "Pinienkerne", "Piñones", "Pinoli", "Pignons de pin", "松子", "Кедровые орехи", "صنوبر") },
            { "Pistachios", T("Pistachios", "Antep fıstığı", "Pistazien", "Pistachos", "Pistacchi", "Pistaches", "开心果", "Фисташки", "فستق") },
            { "Sesame seeds", T("Sesame seeds", "Susam tohumu", "Sesamsamen", "Semillas de sésamo", "Semi di sesamo", "Graines de sésame", "芝麻", "Семена кунжута", "بذور السمسم") },
            { "Chia seeds", T("Chia seeds", "Chia tohumu", "Chiasamen", "Semillas de chía", "Semi di chia", "Graines de chia", "奇亚籽", "Семена чиа", "بذور الشيا") },
            { "Flax seeds", T("Flax seeds", "Keten tohumu", "Leinsamen", "Semillas de lino", "Semi di lino", "Graines de lin", "亚麻籽", "Льняное семя", "بذور الكتان") },
            { "Pumpkin seeds", T("Pumpkin seeds", "Kabak çekirdeği", "Kürbiskerne", "Semillas de calabaza", "Semi di zucca", "Graines de citrouille", "南瓜籽", "Тыквенные семечки", "بذور اليقطين") },
            { "Sunflower seeds", T("Sunflower seeds", "Ay çekirdeği", "Sonnenblumenkerne", "Semillas de girasol", "Semi di girasole", "Graines de tournesol", "葵花籽", "Семена подсолнечника", "بذور عباد الشمس") },
            { "Mixed nuts", T("Mixed nuts", "Karışık kuruyemiş", "Nussmischung", "Frutos secos mixtos", "Frutta secca mista", "Mélange de noix", "混合坚果", "Смесь орехов", "مكسرات مشكلة") },
        });

        // --- Dried and Fresh Fruits ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Dried apricots", T("Dried apricots", "Kuru kayısı", "Getrocknete Aprikosen", "Albaricoques secos", "Albicocche secche", "Abricots secs", "杏干", "Курага", "مشمش مجفف") },
            { "Dried figs", T("Dried figs", "Kuru incir", "Getrocknete Feigen", "Higos secos", "Fichi secchi", "Figues sèches", "无花果干", "Сушеный инжир", "تين مجفف") },
            { "Dried dates", T("Dried dates", "Kuru hurma", "Getrocknete Datteln", "Dátiles secos", "Datteri secchi", "Dattes séchées", "干枣", "Сушеные финики", "تمر مجفف") },
            { "Date paste", T("Date paste", "Hurma ezmesi", "Dattelpaste", "Pasta de dátil", "Pasta di datteri", "Pâte de dattes", "枣泥", "Финиковая паста", "معجون التمر") },
            { "Raisins", T("Raisins", "Kuru üzüm", "Rosinen", "Pasas", "Uvetta", "Raisins secs", "葡萄干", "Изюм", "زبيب") },
            { "Sultanas", T("Sultanas", "Sultani", "Sultaninen", "Pasas sultanas", "Uva sultanina", "Raisins de Corinthe", "苏丹娜葡萄干", "Султана", "زبيب سلطاني") },
            { "Cranberries", T("Cranberries", "Turna yemişi", "Cranberries", "Arándanos rojos", "Mirtilli rossi", "Canneberges", "蔓越莓", "Клюква", "توت بري") },
            { "Dried cherries", T("Dried cherries", "Kuru kiraz", "Getrocknete Kirschen", "Cerezas secas", "Ciliegie secche", "Cerises séchées", "樱桃干", "Сушеная вишня", "كرز مجفف") },
            { "Prunes", T("Prunes", "Kuru erik", "Pflaumen", "Ciruelas pasas", "Prugne secche", "Pruneaux", "西梅", "Чернослив", "برقوق مجفف") },
            { "Dried mango", T("Dried mango", "Kuru mango", "Getrocknete Mango", "Mango seco", "Mango secco", "Mangue séchée", "芒果干", "Сушеное манго", "مانجو مجفف") },
            { "Dried pineapple", T("Dried pineapple", "Kuru ananas", "Getrocknete Ananas", "Piña seca", "Ananas secco", "Ananas séché", "菠萝干", "Сушеный ананас", "أناناس مجفف") },
            { "Dried blueberries", T("Dried blueberries", "Kuru yaban mersini", "Getrocknete Blaubeeren", "Arándanos azules secos", "Mirtilli secchi", "Myrtilles séchées", "蓝莓干", "Сушеная черника", "توت أزرق مجفف") },
            { "Apples", T("Apples", "Elma", "Äpfel", "Manzanas", "Mele", "Pommes", "苹果", "Яблоки", "تفاح") },
            { "Bananas", T("Bananas", "Muz", "Bananen", "Plátanos", "Banane", "Bananes", "香蕉", "Бананы", "موز") },
            { "Pears", T("Pears", "Armut", "Birnen", "Peras", "Pere", "Poires", "梨", "Груши", "كمثرى") },
            { "Oranges", T("Oranges", "Portakal", "Orangen", "Naranjas", "Arance", "Oranges", "橙子", "Апельсины", "برتقال") },
            { "Lemons", T("Lemons", "Limon", "Zitronen", "Limones", "Limoni", "Citrons", "柠檬", "Лимоны", "ليمون") },
            { "Limes", T("Limes", "Misket limonu", "Limetten", "Limas", "Lime", "Limes", "青柠", "Лаймы", "ليم") },
            { "Grapefruits", T("Grapefruits", "Greyfurt", "Grapefruits", "Pomelos", "Pompelmi", "Pamplemousses", "葡萄柚", "Грейпфруты", "جريب فروت") },
            { "Mandarins", T("Mandarins", "Mandalina", "Mandarinen", "Mandarinas", "Mandarini", "Mandarines", "柑橘", "Мандарины", "يوسفي") },
            { "Tangerines", T("Tangerines", "Tangerin", "Tangerinen", "Mandarinas", "Tangerini", "Tangerines", "橘子", "Танжерины", "يوسفي") },
            { "Kiwi", T("Kiwi", "Kivi", "Kiwi", "Kiwi", "Kiwi", "Kiwi", "猕猴桃", "Киви", "كيوي") },
            { "Grapes", T("Grapes", "Üzüm", "Trauben", "Uvas", "Uva", "Raisins", "葡萄", "Виноград", "عنب") },
            { "Red grapes", T("Red grapes", "Kırmızı üzüm", "Rote Trauben", "Uvas rojas", "Uva rossa", "Raisins rouges", "红葡萄", "Красный виноград", "عنب أحمر") },
            { "Green grapes", T("Green grapes", "Yeşil üzüm", "Grüne Trauben", "Uvas verdes", "Uva verde", "Raisins verts", "绿葡萄", "Зеленый виноград", "عنب أخضر") },
            { "Watermelon", T("Watermelon", "Karpuz", "Wassermelone", "Sandía", "Anguria", "Pastèque", "西瓜", "Арбуз", "بطيخ") },
            { "Cantaloupe", T("Cantaloupe", "Kavun", "Cantaloupe-Melone", "Cantalupo", "Melone cantalupo", "Cantaloup", "哈密瓜", "Канталупа", "شمام") },
            { "Honeydew", T("Honeydew", "Kavun", "Honigmelone", "Melón dulce", "Melone a polpa verde", "Melon miel", "蜜瓜", "Медовая дыня", "شمام عسل") },
            { "Pineapple", T("Pineapple", "Ananas", "Ananas", "Piña", "Ananas", "Ananas", "菠萝", "Ананас", "أناناس") },
            { "Papaya", T("Papaya", "Papaya", "Papaya", "Papaya", "Papaya", "Papaye", "木瓜", "Папайя", "بابايا") },
            { "Mango", T("Mango", "Mango", "Mango", "Mango", "Mango", "Mangue", "芒果", "Манго", "مانجو") },
            { "Passionfruit", T("Passionfruit", "Çarkıfelek meyvesi", "Passionsfrucht", "Maracuyá", "Frutto della passione", "Fruit de la passion", "百香果", "Маракуйя", "باشن فروت") },
            { "Guava", T("Guava", "Guava", "Guave", "Guayaba", "Guava", "Goyave", "番石榴", "Гуава", "جوافة") },
            { "Pomegranate", T("Pomegranate", "Nar", "Granatapfel", "Granada", "Melograno", "Grenade", "石榴", "Гранат", "رمان") },
            { "Berries", T("Berries", "Orman meyveleri", "Beeren", "Bayas", "Bacche", "Baies", "浆果", "Ягоды", "توت") },
            { "Strawberries", T("Strawberries", "Çilek", "Erdbeeren", "Fresas", "Fragole", "Fraises", "草莓", "Клубника", "فراولة") },
            { "Blueberries", T("Blueberries", "Yaban mersini", "Blaubeeren", "Arándanos azules", "Mirtilli", "Myrtilles", "蓝莓", "Черника", "توت أزرق") },
            { "Raspberries", T("Raspberries", "Ahududu", "Himbeeren", "Frambuesas", "Lamponi", "Framboises", "覆盆子", "Малина", "توت العليق") },
            { "Blackberries", T("Blackberries", "Böğürtlen", "Brombeeren", "Moras", "More", "Mûres", "黑莓", "Ежевика", "توت أسود") },
            { "Cherries", T("Cherries", "Kiraz", "Kirschen", "Cerezas", "Ciliegie", "Cerises", "樱桃", "Вишня", "كرز") },
            { "Peaches", T("Peaches", "Şeftali", "Pfirsiche", "Melocotones", "Pesche", "Pêches", "桃子", "Персики", "خوخ") },
            { "Plums", T("Plums", "Erik", "Pflaumen", "Ciruelas", "Prugne", "Prunes", "李子", "Сливы", "برقوق") },
            { "Nectarines", T("Nectarines", "Nektarin", "Nektarinen", "Nectarinas", "Nettarine", "Nectarines", "油桃", "Нектарины", "نكتارين") },
            { "Figs", T("Figs", "İncir", "Feigen", "Higos", "Fichi", "Figues", "无花果", "Инжир", "تين") },
        });

        // --- Coconut, Avocado, and Herbs ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Coconut", T("Coconut", "Hindistan cevizi", "Kokosnuss", "Coco", "Cocco", "Noix de coco", "椰子", "Кокос", "جوز الهند") },
            { "Shredded coconut", T("Shredded coconut", "Rendelenmiş hindistan cevizi", "Kokosraspel", "Coco rallado", "Cocco grattugiato", "Noix de coco râpée", "椰丝", "Кокосовая стружка", "جوز الهند المبشور") },
            { "Coconut milk", T("Coconut milk", "Hindistan cevizi sütü", "Kokosmilch", "Leche de coco", "Latte di cocco", "Lait de coco", "椰奶", "Кокосовое молоко", "حليب جوز الهند") },
            { "Coconut cream", T("Coconut cream", "Hindistan cevizi kreması", "Kokoscreme", "Crema de coco", "Crema di cocco", "Crème de coco", "椰子奶油", "Кокосовые сливки", "كريمة جوز الهند") },
            { "Avocado", T("Avocado", "Avokado", "Avocado", "Aguacate", "Avocado", "Avocat", "牛油果", "Авокадо", "أفوكادو") },
            { "Herbs", T("Herbs", "Otlar", "Kräuter", "Hierbas", "Erbe", "Herbes", "香草", "Травы", "أعشاب") },
            { "Basil", T("Basil", "Fesleğen", "Basilikum", "Albahaca", "Basilico", "Basilic", "罗勒", "Базилик", "ريحان") },
            { "Parsley", T("Parsley", "Maydanoz", "Petersilie", "Perejil", "Prezzemolo", "Persil", "欧芹", "Петрушка", "بقدونس") },
            { "Cilantro", T("Cilantro", "Kişniş", "Koriander (Blätter)", "Cilantro", "Coriandolo", "Coriandre", "香菜", "Кинза", "كزبرة") },
            { "Mint", T("Mint", "Nane", "Minze", "Menta", "Menta", "Menthe", "薄荷", "Мята", "نعناع") },
            { "Dill", T("Dill", "Dereotu", "Dill", "Eneldo", "Aneto", "Aneth", "莳萝", "Укроп", "شبت") },
            { "Thyme", T("Thyme", "Kekik", "Thymian", "Tomillo", "Timo", "Thym", "百里香", "Тимьян", "زعتر") },
            { "Oregano", T("Oregano", "Kekik", "Oregano", "Orégano", "Origano", "Origan", "牛至", "Орегано", "أوريجانو") },
            { "Rosemary", T("Rosemary", "Biberiye", "Rosmarin", "Romero", "Rosmarino", "Romarin", "迷迭香", "Розмарин", "إكليل الجبل") },
            { "Marjoram", T("Marjoram", "Mercanköşk", "Majoran", "Mejorana", "Maggiorana", "Marjolaine", "马郁兰", "Майоран", "مردقوش") },
            { "Sage", T("Sage", "Adaçayı", "Salbei", "Salvia", "Salvia", "Sauge", "鼠尾草", "Шалфей", "ميرمية") },
            { "Tarragon", T("Tarragon", "Tarhun", "Estragon", "Estragón", "Dragoncello", "Estragon", "龙蒿", "Эстрагон", "طرخون") },
            { "Chives", T("Chives", "Frenk soğanı", "Schnittlauch", "Cebollino", "Erba cipollina", "Ciboulette", "细香葱", "Шнитт-лук", "ثوم معمر") },
            { "Bay leaves", T("Bay leaves", "Defne yaprağı", "Lorbeerblätter", "Hojas de laurel", "Foglie di alloro", "Feuilles de laurier", "月桂叶", "Лавровый лист", "ورق الغار") },
            { "Curry leaves", T("Curry leaves", "Köri yaprağı", "Curryblätter", "Hojas de curry", "Foglie di curry", "Feuilles de curry", "咖喱叶", "Листья карри", "أوراق الكاري") },
            { "Lemongrass", T("Lemongrass", "Limon otu", "Zitronengras", "Hierba limón", "Citronella", "Citronnelle", "柠檬草", "Лемонграсс", "عشب الليمون") },
        });

        // --- Spices: Heat and Pepper ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Chili flakes", T("Chili flakes", "Pul biber", "Chiliflocken", "Copos de chile", "Peperoncino in fiocchi", "Flocons de piment", "辣椒片", "Хлопья чили", "رقائق الفلفل الحار") },
            { "Aleppo pepper", T("Aleppo pepper", "Halep biberi", "Aleppo-Pfeffer", "Pimienta de Alepo", "Pepe di Aleppo", "Piment d'Alep", "阿勒颇辣椒", "Перец Алеппо", "فلفل حلب") },
            { "Isot pepper", T("Isot pepper", "İsot biberi", "Isot-Pfeffer", "Pimienta Isot", "Pepe Isot", "Piment Isot", "伊索特辣椒", "Перец Исот", "فلفل أسود مجفف") },
            { "Pul biber", T("Pul biber", "Pul biber", "Pul Biber", "Pul biber", "Peperoncino turco", "Piment Pul Biber", "土耳其辣椒粉", "Пул бибер", "فلفل بول") },
            { "White pepper", T("White pepper", "Beyaz biber", "Weißer Pfeffer", "Pimienta blanca", "Pepe bianco", "Poivre blanc", "白胡椒", "Белый перец", "فلفل أبيض") },
            { "Black pepper", T("Black pepper", "Karabiber", "Schwarzer Pfeffer", "Pimienta negra", "Pepe nero", "Poivre noir", "黑胡椒", "Черный перец", "فلفل أسود") },
            { "Green peppercorn", T("Green peppercorn", "Yeşil tane biber", "Grüne Pfefferkörner", "Pimienta verde", "Pepe verde", "Poivre vert", "绿胡椒粒", "Зеленый перец горошком", "فلفل أخضر") },
            { "Pink peppercorn", T("Pink peppercorn", "Pembe tane biber", "Rosa Pfefferkörner", "Pimienta rosa", "Pepe rosa", "Poivre rose", "粉红胡椒粒", "Розовый перец горошком", "فلفل وردي") },
        });

        // --- Spices: Earthy and Aromatic ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Cumin", T("Cumin", "Kimyon", "Kreuzkümmel", "Comino", "Cumino", "Cumin", "孜然", "Кумин", "كمون") },
            { "Coriander seeds", T("Coriander seeds", "Kişniş tohumu", "Koriandersamen", "Semillas de cilantro", "Semi di coriandolo", "Graines de coriandre", "香菜籽", "Семена кориандра", "بذور الكزبرة") },
            { "Fennel seeds", T("Fennel seeds", "Rezene tohumu", "Fenchelsamen", "Semillas de hinojo", "Semi di finocchio", "Graines de fenouil", "茴香籽", "Семена фенхеля", "بذور الشمر") },
            { "Anise seeds", T("Anise seeds", "Anason tohumu", "Anissamen", "Semillas de anís", "Semi di anice", "Graines d'anis", "茴香", "Анисовое семя", "بذور اليانسون") },
            { "Mustard seeds", T("Mustard seeds", "Hardal tohumu", "Senfsaat", "Semillas de mostaza", "Semi di senape", "Graines de moutarde", "芥菜籽", "Горчичное семя", "بذور الخردل") },
            { "Caraway seeds", T("Caraway seeds", "Kimyon", "Kümmel", "Alcaravea", "Cumino dei prati", "Cumin des prés", "香菜籽", "Тмин", "كراوية") },
            { "Fenugreek", T("Fenugreek", "Çemen otu", "Bockshornklee", "Fenogreco", "Fieno greco", "Fenugrec", "葫芦巴", "Пажитник", "الحلبة") },
            { "Paprika", T("Paprika", "Toz biber", "Paprika", "Pimentón", "Paprica", "Paprika", "辣椒粉", "Паприка", "بابريكا") },
            { "Smoked paprika", T("Smoked paprika", "İsli biber", "Geräuchertes Paprikapulver", "Pimentón ahumado", "Paprica affumicata", "Paprika fumé", "烟熏辣椒粉", "Копченая паприка", "بابريكا مدخنة") },
            { "Turmeric", T("Turmeric", "Zerdeçal", "Kurkuma", "Cúrcuma", "Curcuma", "Curcuma", "姜黄", "Куркума", "كركم") },
            { "Saffron", T("Saffron", "Safran", "Safran", "Azafrán", "Zafferano", "Safran", "藏红花", "Шафран", "زعفران") },
            { "Cardamom", T("Cardamom", "Kakule", "Kardamom", "Cardamomo", "Cardamomo", "Cardamome", "豆蔻", "Кардамон", "هيل") },
            { "Cloves", T("Cloves", "Karanfil", "Nelken", "Clavos", "Chiodi di garofano", "Clous de girofle", "丁香", "Гвоздика", "قرنفل") },
            { "Allspice", T("Allspice", "Yenibahar", "Piment", "Pimienta de Jamaica", "Pimento", "Piment de Jamaïque", "多香果", "Душистый перец", "بهار حلو") },
            { "Nutmeg", T("Nutmeg", "Muskat", "Muskatnuss", "Nuez moscada", "Noce moscata", "Noix de muscade", "肉豆蔻", "Мускатный орех", "جوزة الطيب") },
            { "Mace", T("Mace", "Muskat kabuğu", "Macis", "Macis", "Mace", "Macis", "肉豆蔻衣", "Мацис", "قشرة جوزة الطيب") },
            { "Cinnamon", T("Cinnamon", "Tarçın", "Zimt", "Canela", "Cannella", "Cannelle", "肉桂", "Корица", "قرفة") },
            { "Ginger powder", T("Ginger powder", "Zencefil tozu", "Ingwerpulver", "Jengibre en polvo", "Zenzero in polvere", "Gingembre en poudre", "姜粉", "Имбирный порошок", "مسحوق الزنجبيل") },
            { "Garlic powder", T("Garlic powder", "Sarımsak tozu", "Knoblauchpulver", "Ajo en polvo", "Aglio in polvere", "Ail en poudre", "大蒜粉", "Чесночный порошок", "مسحوق الثوم") },
            { "Onion powder", T("Onion powder", "Soğan tozu", "Zwiebelpulver", "Cebolla en polvo", "Cipolla in polvere", "Oignon en poudre", "洋葱粉", "Луковый порошок", "مسحوق البصل") },
            { "Sumac", T("Sumac", "Sumak", "Sumach", "Zumaque", "Sommacco", "Sumac", "漆树粉", "Сумах", "سماق") },
        });

        // --- Spice Blends and Seasonings ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Zaatar", T("Zaatar", "Zahter", "Za'atar", "Za'atar", "Za'atar", "Zaatar", "扎塔尔", "Заатар", "زعتر") },
            { "Baharat", T("Baharat", "Baharat", "Baharat", "Baharat", "Baharat", "Baharat", "巴哈拉特香料", "Бахарат", "بهارات") },
            { "Seven spice", T("Seven spice", "Yedi baharat", "Sieben Gewürze", "Siete especias", "Sette spezie", "Sept épices", "七味香料", "Семь специй", "سبع بهارات") },
            { "Shawarma spice", T("Shawarma spice", "Şavarma baharatı", "Shawarma-Gewürz", "Especias shawarma", "Spezie shawarma", "Épices chawarma", "沙威玛香料", "Специя для шаурмы", "بهارات الشاورما") },
            { "Cajun seasoning", T("Cajun seasoning", "Cajun baharatı", "Cajun-Gewürz", "Condimento cajún", "Condimento cajun", "Assaisonnement cajun", "卡宴调味料", "Каджунская приправа", "توابل الكاجون") },
            { "Ranch seasoning", T("Ranch seasoning", "Ranch baharatı", "Ranch-Gewürz", "Condimento ranch", "Condimento ranch", "Assaisonnement ranch", "牧场调味料", "Ранч приправа", "توابل الرانش") },
            { "Taco seasoning", T("Taco seasoning", "Taco baharatı", "Taco-Gewürz", "Condimento para tacos", "Condimento per taco", "Assaisonnement à tacos", "塔可调味料", "Тако приправа", "توابل التاكو") },
            { "Italian seasoning", T("Italian seasoning", "İtalyan baharatı", "Italienische Kräutermischung", "Condimento italiano", "Condimento italiano", "Assaisonnement italien", "意大利调味料", "Итальянские травы", "توابل إيطالية") },
            { "Herbes de provence", T("Herbes de provence", "Provence otları", "Kräuter der Provence", "Hierbas de Provenza", "Erbe di Provenza", "Herbes de Provence", "普罗旺斯香草", "Прованские травы", "أعشاب بروفانس") },
        });

        // --- Liquids: Vinegars, Juices, and Water ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Vinegar", T("Vinegar", "Sirke", "Essig", "Vinagre", "Aceto", "Vinaigre", "醋", "Уксус", "خل") },
            { "White vinegar", T("White vinegar", "Beyaz sirke", "Weißweinessig", "Vinagre blanco", "Aceto bianco", "Vinaigre blanc", "白醋", "Белый уксус", "خل أبيض") },
            { "Apple cider vinegar", T("Apple cider vinegar", "Elma sirkesi", "Apfelessig", "Vinagre de sidra de manzana", "Aceto di mele", "Vinaigre de cidre", "苹果醋", "Яблочный уксус", "خل التفاح") },
            { "Red wine vinegar", T("Red wine vinegar", "Kırmızı şarap sirkesi", "Rotweinessig", "Vinagre de vino tinto", "Aceto di vino rosso", "Vinaigre de vin rouge", "红酒醋", "Красный винный уксус", "خل النبيذ الأحمر") },
            { "Balsamic vinegar", T("Balsamic vinegar", "Balzamik sirke", "Balsamico-Essig", "Vinagre balsámico", "Aceto balsamico", "Vinaigre balsamique", "香醋", "Бальзамический уксус", "خل بلسمي") },
            { "Rice vinegar", T("Rice vinegar", "Pirinç sirkesi", "Reisessig", "Vinagre de arroz", "Aceto di riso", "Vinaigre de riz", "米醋", "Рисовый уксус", "خل الأرز") },
            { "Malt vinegar", T("Malt vinegar", "Malt sirkesi", "Malzessig", "Vinagre de malta", "Aceto di malto", "Vinaigre de malt", "麦芽醋", "Солодовый уксус", "خل الشعير") },
            { "Lemon juice", T("Lemon juice", "Limon suyu", "Zitronensaft", "Jugo de limón", "Succo di limone", "Jus de citron", "柠檬汁", "Лимонный сок", "عصير الليمون") },
            { "Lime juice", T("Lime juice", "Misket limonu suyu", "Limettensaft", "Jugo de lima", "Succo di lime", "Jus de lime", "青柠汁", "Сок лайма", "عصير الليم") },
            { "Orange juice", T("Orange juice", "Portakal suyu", "Orangensaft", "Jugo de naranja", "Succo d'arancia", "Jus d'orange", "橙汁", "Апельсиновый сок", "عصير البرتقال") },
            { "Pomegranate juice", T("Pomegranate juice", "Nar suyu", "Granatapfelsaft", "Jugo de granada", "Succo di melograno", "Jus de grenade", "石榴汁", "Гранатовый сок", "عصير الرمان") },
            { "Tomato juice", T("Tomato juice", "Domates suyu", "Tomatensaft", "Jugo de tomate", "Succo di pomodoro", "Jus de tomate", "番茄汁", "Томатный сок", "عصير الطماطم") },
            { "Beet juice", T("Beet juice", "Pancar suyu", "Rübensaft", "Jugo de remolacha", "Succo di barbabietola", "Jus de betterave", "甜菜汁", "Свекольный сок", "عصير الشمندر") },
            { "Pickle brine", T("Pickle brine", "Turşu suyu", "Einlegeflüssigkeit", "Salmuera de encurtidos", "Salamoia di sottaceti", "Saumure de cornichons", "腌菜盐水", "Рассол", "محلول ملحي") },
            { "Soy milk", T("Soy milk", "Soya sütü", "Sojamilch", "Leche de soja", "Latte di soia", "Lait de soja", "豆奶", "Соевое молоко", "حليب الصويا") },
            { "Almond milk", T("Almond milk", "Badem sütü", "Mandelmilch", "Leche de almendras", "Latte di mandorle", "Lait d'amande", "杏仁奶", "Миндальное молоко", "حليب اللوز") },
            { "Oat milk", T("Oat milk", "Yulaf sütü", "Hafermilch", "Leche de avena", "Latte d'avena", "Lait d'avoine", "燕麦奶", "Овсяное молоко", "حليب الشوفان") },
            { "Rice milk", T("Rice milk", "Pirinç sütü", "Reismilch", "Leche de arroz", "Latte di riso", "Lait de riz", "米浆", "Рисовое молоко", "حليب الأرز") },
            { "Coconut water", T("Coconut water", "Hindistan cevizi suyu", "Kokoswasser", "Agua de coco", "Acqua di cocco", "Eau de coco", "椰子水", "Кокосовая вода", "ماء جوز الهند") },
            { "Sparkling water", T("Sparkling water", "Sade soda", "Sprudelwasser", "Agua con gas", "Acqua frizzante", "Eau pétillante", "苏打水", "Газированная вода", "مياه غازية") },
            { "Tonic water", T("Tonic water", "Tonik", "Tonic Water", "Agua tónica", "Acqua tonica", "Eau tonique", "奎宁水", "Тоник", "مياه التونيك") },
            { "Cola", T("Cola", "Kola", "Cola", "Cola", "Cola", "Cola", "可乐", "Кола", "كولا") },
            { "Lemon soda", T("Lemon soda", "Limonlu gazoz", "Zitronenlimonade", "Refresco de limón", "Gassosa al limone", "Limonade", "柠檬汽水", "Лимонад", "صودا الليمون") },
            { "Orange soda", T("Orange soda", "Portakallı gazoz", "Orangenlimonade", "Refresco de naranja", "Gassosa all'arancia", "Jus d'orange pétillant", "橙味汽水", "Апельсиновый лимонад", "صودا البرتقال") },
            { "Grenadine", T("Grenadine", "Nar şurubu", "Grenadine", "Granadina", "Granatina", "Grenadine", "红石榴糖浆", "Гренадин", "غرينادين") },
        });

        // --- Beverages, Coffee, and Chocolate ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Tea", T("Tea", "Çay", "Tee", "Té", "Tè", "Thé", "茶", "Чай", "شاي") },
            { "Black tea", T("Black tea", "Siyah çay", "Schwarzer Tee", "Té negro", "Tè nero", "Thé noir", "红茶", "Черный чай", "شاي أسود") },
            { "Green tea", T("Green tea", "Yeşil çay", "Grüner Tee", "Té verde", "Tè verde", "Thé vert", "绿茶", "Зеленый чай", "شاي أخضر") },
            { "White tea", T("White tea", "Beyaz çay", "Weißer Tee", "Té blanco", "Tè bianco", "Thé blanc", "白茶", "Белый чай", "شاي أبيض") },
            { "Earl grey", T("Earl grey", "Earl Grey", "Earl Grey", "Earl Grey", "Earl Grey", "Earl Grey", "伯爵茶", "Эрл Грей", "إيرل جراي") },
            { "Hibiscus tea", T("Hibiscus tea", "Hibiskus çayı", "Hibiskustee", "Té de hibisco", "Tè all'ibisco", "Tisane d'hibiscus", "芙蓉花茶", "Чай каркаде", "شاي الكركديه") },
            { "Mint tea", T("Mint tea", "Nane çayı", "Minztee", "Té de menta", "Tè alla menta", "Thé à la menthe", "薄荷茶", "Мятный чай", "شاي النعناع") },
            { "Coffee beans", T("Coffee beans", "Kahve çekirdekleri", "Kaffeebohnen", "Granos de café", "Chicchi di caffè", "Grains de café", "咖啡豆", "Кофейные зерна", "حبوب البن") },
            { "Arabica coffee", T("Arabica coffee", "Arabica kahvesi", "Arabica-Kaffee", "Café arábica", "Caffè arabica", "Café arabica", "阿拉比卡咖啡", "Арабика", "قهوة أرابيكا") },
            { "Espresso beans", T("Espresso beans", "Espresso çekirdekleri", "Espressobohnen", "Granos de espresso", "Chicchi di espresso", "Grains d'espresso", "浓缩咖啡豆", "Эспрессо зерна", "حبوب الإسبريسو") },
            { "Instant coffee", T("Instant coffee", "Hazır kahve", "Instantkaffee", "Café instantáneo", "Caffè istantaneo", "Café instantané", "速溶咖啡", "Растворимый кофе", "قهوة سريعة الذوبان") },
            { "Cocoa powder", T("Cocoa powder", "Kakao tozu", "Kakaopulver", "Cacao en polvo", "Cacao in polvere", "Cacao en poudre", "可可粉", "Какао-порошок", "مسحوق الكاكاو") },
            { "Dark chocolate", T("Dark chocolate", "Bitter çikolata", "Zartbitterschokolade", "Chocolate negro", "Cioccolato fondente", "Chocolat noir", "黑巧克力", "Темный шоколад", "شوكولاتة داكنة") },
            { "Milk chocolate", T("Milk chocolate", "Sütlü çikolata", "Milchschokolade", "Chocolate con leche", "Cioccolato al latte", "Chocolat au lait", "牛奶巧克力", "Молочный шоколад", "شوكولاتة بالحليب") },
            { "White chocolate", T("White chocolate", "Beyaz çikolata", "Weiße Schokolade", "Chocolate blanco", "Cioccolato bianco", "Chocolat blanc", "白巧克力", "Белый шоколад", "شوكولاتة بيضاء") },
            { "Chocolate chips", T("Chocolate chips", "Çikolata damlası", "Schokoladenstückchen", "Chispas de chocolate", "Gocce di cioccolato", "Pépites de chocolat", "巧克力片", "Шоколадная крошка", "رقائق الشوكولاتة") },
        });

        // --- Sweet Sauces and Extracts ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Caramel sauce", T("Caramel sauce", "Karamel sos", "Karamellsauce", "Salsa de caramelo", "Salsa al caramello", "Sauce caramel", "焦糖酱", "Карамельный соус", "صلصة الكراميل") },
            { "Toffee", T("Toffee", "Toffee", "Toffee", "Toffee", "Toffee", "Toffee", "太妃糖", "Ирис", "توفي") },
            { "Sprinkles", T("Sprinkles", "Pasta süsü", "Streusel", "Chispas de colores", "Codette di zucchero", "Vermicelles", "洒水", "Посыпка", "حلويات للتزيين") },
            { "Vanilla extract", T("Vanilla extract", "Vanilya özü", "Vanilleextrakt", "Extracto de vainilla", "Estratto di vaniglia", "Extrait de vanille", "香草精", "Ванильный экстракт", "خلاصة الفانيليا") },
            { "Almond extract", T("Almond extract", "Badem özü", "Mandelaroma", "Extracto de almendra", "Estratto di mandorla", "Extrait d'amande", "杏仁精", "Миндальный экстракт", "خلاصة اللوز") },
            { "Rose water", T("Rose water", "Gül suyu", "Rosenwasser", "Agua de rosas", "Acqua di rose", "Eau de rose", "玫瑰水", "Розовая вода", "ماء الورد") },
            { "Orange blossom water", T("Orange blossom water", "Portakal çiçeği suyu", "Orangenblütenwasser", "Agua de azahar", "Acqua di fiori d'arancio", "Eau de fleur d'oranger", "橙花水", "Вода апельсинового цвета", "ماء الزهر") },
            { "Gelatin", T("Gelatin", "Jelatin", "Gelatine", "Gelatina", "Gelatina", "Gélatine", "明胶", "Желатин", "جيلاتين") },
            { "Agar agar", T("Agar agar", "Agar agar", "Agar-Agar", "Agar-agar", "Agar-agar", "Agar-agar", "琼脂", "Агар-агар", "أجار أجار") },
            { "Corn syrup", T("Corn syrup", "Mısır şurubu", "Maissirup", "Sirope de maíz", "Sciroppo di mais", "Sirop de maïs", "玉米糖浆", "Кукурузный сироп", "شراب الذرة") },
            { "Custard powder", T("Custard powder", "Muhallebi tozu", "Puddingpulver", "Polvo de natillas", "Preparato per crema pasticcera", "Poudre à flan", "蛋奶冻粉", "Порошок для заварного крема", "مسحوق الكاسترد") },
            { "Pudding mix", T("Pudding mix", "Puding karışımı", "Puddingmischung", "Mezcla para pudin", "Preparato per budino", "Préparation pour pudding", "布丁混合物", "Смесь для пудинга", "خليط البودينغ") },
            { "Cake flour", T("Cake flour", "Kek unu", "Kuchenmehl", "Harina para pasteles", "Farina per dolci", "Farine à gâteaux", "蛋糕粉", "Кондитерская мука", "دقيق الكيك") },
            { "Bread improver", T("Bread improver", "Ekmek geliştirici", "Brotverbesserer", "Mejorador de pan", "Miglioratore per pane", "Améliorant pour pain", "面包改良剂", "Улучшитель хлеба", "محسن الخبز") },
        });

        // --- Stocks, Broths, and Savory Pastes ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Beef stock", T("Beef stock", "Dana suyu", "Rinderbrühe", "Caldo de res", "Brodo di manzo", "Bouillon de bœuf", "牛肉高汤", "Говяжий бульон", "مرق لحم بقري") },
            { "Chicken stock", T("Chicken stock", "Tavuk suyu", "Hühnerbrühe", "Caldo de pollo", "Brodo di pollo", "Bouillon de poulet", "鸡肉高汤", "Куриный бульон", "مرق دجاج") },
            { "Vegetable stock", T("Vegetable stock", "Sebze suyu", "Gemüsebrühe", "Caldo de verduras", "Brodo vegetale", "Bouillon de légumes", "蔬菜高汤", "Овощной бульон", "مرق خضروات") },
            { "Fish stock", T("Fish stock", "Balık suyu", "Fischfond", "Caldo de pescado", "Brodo di pesce", "Fumet de poisson", "鱼高汤", "Рыбный бульон", "مرق سمك") },
            { "Bouillon cubes", T("Bouillon cubes", "Bulyon küpleri", "Brühwürfel", "Cubos de caldo", "Dadi da brodo", "Cubes de bouillon", "浓汤块", "Бульонные кубики", "مكعبات مرق") },
            { "Tomato paste", T("Tomato paste", "Salça", "Tomatenmark", "Pasta de tomate", "Concentrato di pomodoro", "Concentré de tomate", "番茄酱", "Томатная паста", "معجون طماطم") },
            { "Pepper paste", T("Pepper paste", "Biber salçası", "Paprikamark", "Pasta de pimiento", "Concentrato di peperoni", "Concentré de poivron", "辣椒酱", "Перечная паста", "معجون الفلفل") },
            { "Harissa", T("Harissa", "Harissa", "Harissa", "Harissa", "Harissa", "Harissa", "哈里萨辣酱", "Харисса", "هريسة") },
            { "Chili paste", T("Chili paste", "Acı biber salçası", "Chilipaste", "Pasta de chile", "Pasta di peperoncino", "Pâte de piment", "辣椒酱", "Перечная паста", "معجون الفلفل الحار") },
        });

        // --- Condiments and Dressings ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Mustard", T("Mustard", "Hardal", "Senf", "Mostaza", "Senape", "Moutarde", "芥末", "Горчица", "خردل") },
            { "Dijon mustard", T("Dijon mustard", "Dijon hardalı", "Dijon-Senf", "Mostaza de Dijon", "Senape di Digione", "Moutarde de Dijon", "第戎芥末", "Дижонская горчица", "خردل ديجون") },
            { "Whole grain mustard", T("Whole grain mustard", "Tane hardal", "Grobkörniger Senf", "Mostaza a la antigua", "Senape in grani", "Moutarde à l'ancienne", "全粒芥末", "Зернистая горчица", "خردل الحبوب الكاملة") },
            { "Ketchup", T("Ketchup", "Ketçap", "Ketchup", "Kétchup", "Ketchup", "Ketchup", "番茄酱", "Кетчуп", "كاتشب") },
            { "Mayonnaise", T("Mayonnaise", "Mayonez", "Mayonnaise", "Mayonesa", "Maionese", "Mayonnaise", "蛋黄酱", "Майонез", "مايونيز") },
            { "Aioli", T("Aioli", "Aioli", "Aioli", "Alioli", "Salsa all'aglio", "Aïoli", "蒜泥蛋黄酱", "Айоли", "أيولي") },
            { "Garlic sauce", T("Garlic sauce", "Sarımsak sosu", "Knoblauchsauce", "Salsa de ajo", "Salsa all'aglio", "Sauce à l'ail", "蒜蓉酱", "Чесночный соус", "صلصة الثوم") },
            { "Toum", T("Toum", "Toum", "Toum", "Toum", "Toum", "Toum", "蒜泥", "Тум", "ثومية") },
            { "Barbecue sauce", T("Barbecue sauce", "Barbekü sosu", "Barbecue-Sauce", "Salsa barbacoa", "Salsa barbecue", "Sauce barbecue", "烧烤酱", "Соус барбекю", "صلصة الشواء") },
            { "Buffalo sauce", T("Buffalo sauce", "Buffalo sosu", "Buffalo-Sauce", "Salsa búfalo", "Salsa buffalo", "Sauce Buffalo", "布法罗辣酱", "Соус Баффало", "صلصة بافلو") },
            { "Ranch dressing", T("Ranch dressing", "Ranch sosu", "Ranch-Dressing", "Aderezo ranch", "Condimento ranch", "Vinaigrette ranch", "牧场沙拉酱", "Ранч-дрессинг", "صلصة الرانش") },
            { "Caesar dressing", T("Caesar dressing", "Sezar sosu", "Caesar-Dressing", "Aderezo César", "Condimento Caesar", "Vinaigrette César", "凯撒沙拉酱", "Цезарь-дрессинг", "صلصة السيزر") },
            { "Italian dressing", T("Italian dressing", "İtalyan sosu", "Italienisches Dressing", "Aderezo italiano", "Condimento italiano", "Vinaigrette italienne", "意大利沙拉酱", "Итальянский дрессинг", "صلصة إيطالية") },
            { "Hot sauce", T("Hot sauce", "Acı sos", "Scharfe Sauce", "Salsa picante", "Salsa piccante", "Sauce piquante", "辣酱", "Острый соус", "صلصة حارة") },
            { "Sriracha", T("Sriracha", "Sriracha", "Sriracha", "Sriracha", "Sriracha", "Sriracha", "是拉差", "Срирача", "صلصة سريراتشا") },
            { "Tabasco", T("Tabasco", "Tabasco", "Tabasco", "Tabasco", "Tabasco", "Tabasco", "塔巴斯科辣椒酱", "Табаско", "تاباسكو") },
            { "Worcestershire sauce", T("Worcestershire sauce", "Worcestershire sosu", "Worcestershiresauce", "Salsa Worcestershire", "Salsa Worcestershire", "Sauce Worcestershire", "伍斯特郡酱", "Вустерширский соус", "صلصة وورشيستر") },
        });

        // --- Coatings and Dippers ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Panko breadcrumbs", T("Panko breadcrumbs", "Panko galeta unu", "Panko-Paniermehl", "Pan rallado Panko", "Pangrattato panko", "Chapelure Panko", "面包糠", "Панко сухари", "فتات الخبز بانكو") },
            { "Regular breadcrumbs", T("Regular breadcrumbs", "Galeta unu", "Paniermehl", "Pan rallado", "Pangrattato", "Chapelure", "普通面包屑", "Обычные сухари", "فتات الخبز العادي") },
            { "Croutons", T("Croutons", "Kruton", "Croutons", "Crutones", "Crostoni", "Croûtons", "烤面包块", "Гренки", "قطع خبز مقلية") },
            { "Tortilla chips", T("Tortilla chips", "Tortilla cipsi", "Tortilla-Chips", "Chips de tortilla", "Tortilla chips", "Chips de tortilla", "玉米片", "Чипсы тортилья", "رقائق التورتيلا") },
            { "Nacho chips", T("Nacho chips", "Nacho cipsi", "Nacho-Chips", "Nachos", "Nachos", "Nachos", "玉米片", "Начос", "رقائق الناتشو") },
            { "Breadsticks", T("Breadsticks", "Galeta", "Grissini", "Palitos de pan", "Grissini", "Gressins", "面包棒", "Хлебные палочки", "أعواد الخبز") },
            { "Crackers", T("Crackers", "Kraker", "Cracker", "Galletas saladas", "Crackers", "Biscuits salés", "饼干", "Крекеры", "مقرمشات") },
            { "Pretzels", T("Pretzels", "Pretzel", "Brezeln", "Pretzels", "Pretzel", "Bretzels", "椒盐卷饼", "Крендели", "بريتزلز") },
        });

        // --- Sauces and Glazes ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Pizza sauce", T("Pizza sauce", "Pizza sosu", "Pizzasauce", "Salsa de pizza", "Salsa per pizza", "Sauce à pizza", "披萨酱", "Соус для пиццы", "صلصة البيتزا") },
            { "Marinara sauce", T("Marinara sauce", "Marinara sosu", "Marinara-Sauce", "Salsa marinara", "Salsa marinara", "Sauce marinara", "马力纳拉酱", "Соус Маринара", "صلصة مارينارا") },
            { "Alfredo sauce", T("Alfredo sauce", "Alfredo sosu", "Alfredo-Sauce", "Salsa Alfredo", "Salsa Alfredo", "Sauce Alfredo", "阿尔弗雷多酱", "Соус Альфредо", "صلصة ألفريدو") },
            { "Pesto", T("Pesto", "Pesto", "Pesto", "Pesto", "Pesto", "Pesto", "香蒜酱", "Песто", "بيستو") },
            { "Sun-dried tomato pesto", T("Sun-dried tomato pesto", "Güneşte kurutulmuş domatesli pesto", "Pesto aus getrockneten Tomaten", "Pesto de tomate seco", "Pesto di pomodori secchi", "Pesto de tomates séchées", "干番茄香蒜酱", "Песто из вяленых томатов", "بيستو الطماطم المجففة") },
            { "Béchamel", T("Béchamel", "Beşamel sos", "Béchamelsauce", "Salsa bechamel", "Salsa besciamella", "Sauce béchamel", "白酱", "Бешамель", "صلصة البشاميل") },
            { "White sauce", T("White sauce", "Beyaz sos", "Weiße Sauce", "Salsa blanca", "Salsa bianca", "Sauce blanche", "白酱", "Белый соус", "صلصة بيضاء") },
            { "Gravy", T("Gravy", "Sulu yemek sosu", "Bratensoße", "Salsa de carne", "Salsa di carne", "Jus de viande", "肉汁", "Подлива", "مرق") },
            { "Brown sauce", T("Brown sauce", "Kahverengi sos", "Braune Sauce", "Salsa marrón", "Salsa marrone", "Sauce brune", "棕色酱", "Коричневый соус", "صلصة بنية") },
            { "Balsamic glaze", T("Balsamic glaze", "Balzamik sır", "Balsamico-Glasur", "Glaseado balsámico", "Glassa di balsamico", "Glaçage balsamique", "香醋釉", "Бальзамическая глазурь", "تزجيج بلسمي") },
            { "Truffle oil", T("Truffle oil", "Trüf yağı", "Trüffelöl", "Aceite de trufa", "Olio al tartufo", "Huile de truffe", "松露油", "Трюфельное масло", "زيت الكمأة") },
            { "Black truffle", T("Black truffle", "Siyah trüf", "Schwarze Trüffel", "Trufa negra", "Tartufo nero", "Truffe noire", "黑松露", "Черный трюфель", "كمأة سوداء") },
            { "White truffle", T("White truffle", "Beyaz trüf", "Weiße Trüffel", "Trufa blanca", "Tartufo bianco", "Truffe blanche", "白松露", "Белый трюфель", "كمأة بيضاء") },
        });

        // --- Pickled, Fermented, and Preserved ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Capers", T("Capers", "Kapari", "Kapern", "Alcaparras", "Capperi", "Câpres", "刺山柑", "Каперсы", "كبار") },
            { "Olives", T("Olives", "Zeytin", "Oliven", "Aceitunas", "Olive", "Olives", "橄榄", "Оливки", "زيتون") },
            { "Green olives", T("Green olives", "Yeşil zeytin", "Grüne Oliven", "Aceitunas verdes", "Olive verdi", "Olives vertes", "绿橄榄", "Зеленые оливки", "زيتون أخضر") },
            { "Black olives", T("Black olives", "Siyah zeytin", "Schwarze Oliven", "Aceitunas negras", "Olive nere", "Olives noires", "黑橄榄", "Черные оливки", "زيتون أسود") },
            { "Kalamata olives", T("Kalamata olives", "Kalamata zeytini", "Kalamata-Oliven", "Aceitunas Kalamata", "Olive Kalamata", "Olives de Kalamata", "卡拉马塔橄榄", "Оливки Каламата", "زيتون كالاماتا") },
            { "Stuffed olives", T("Stuffed olives", "Dolmalı zeytin", "Gefüllte Oliven", "Aceitunas rellenas", "Olive ripiene", "Olives farcies", "酿橄榄", "Фаршированные оливки", "زيتون محشي") },
            { "Pickles", T("Pickles", "Turşu", "Essiggurken", "Pepinillos encurtidos", "Sottaceti", "Cornichons", "腌菜", "Соленья", "مخللات") },
            { "Dill pickles", T("Dill pickles", "Dereotlu turşu", "Dill-Gurken", "Pepinillos agridulces", "Sottaceti all'aneto", "Cornichons à l'aneth", "莳萝腌菜", "Маринованные огурцы с укропом", "مخلل الشبت") },
            { "Sweet pickles", T("Sweet pickles", "Tatlı turşu", "Süße Gurken", "Pepinillos dulces", "Sottaceti dolci", "Cornichons aigres-doux", "甜腌菜", "Сладкие соленья", "مخلل حلو") },
            { "Kraut", T("Kraut", "Lahana turşusu", "Krautsalat", "Chucrut", "Crauti", "Choucroute", "酸菜", "Квашеная капуста", "ملفوف مخلل") },
            { "Fermented chili", T("Fermented chili", "Fermente acı biber", "Fermentierter Chili", "Chile fermentado", "Peperoncino fermentato", "Piment fermenté", "发酵辣椒", "Ферментированный чили", "فلفل حار مخمر") },
            { "Sesame paste", T("Sesame paste", "Susam ezmesi", "Sesampaste", "Pasta de sésamo", "Pasta di sesamo", "Pâte de sésame", "芝麻酱", "Кунжутная паста", "معجون السمسم") },
            { "Black sesame paste", T("Black sesame paste", "Siyah susam ezmesi", "Schwarze Sesampaste", "Pasta de sésamo negro", "Pasta di sesamo nero", "Pâte de sésame noir", "黑芝麻酱", "Черная кунжутная паста", "معجون السمسم الأسود") },
        });

        // --- Jams, Curries, and Specialty Pastes ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Fruit jam", T("Fruit jam", "Meyve reçeli", "Fruchtaufstrich", "Mermelada de fruta", "Marmellata di frutta", "Confiture de fruits", "果酱", "Фруктовый джем", "مربى الفاكهة") },
            { "Strawberry jam", T("Strawberry jam", "Çilek reçeli", "Erdbeermarmelade", "Mermelada de fresa", "Marmellata di fragole", "Confiture de fraises", "草莓酱", "Клубничный джем", "مربى الفراولة") },
            { "Apricot jam", T("Apricot jam", "Kayısı reçeli", "Aprikosenmarmelade", "Mermelada de albaricoque", "Marmellata di albicocche", "Confiture d'abricots", "杏子酱", "Абрикосовый джем", "مربى المشمش") },
            { "Fig jam", T("Fig jam", "İncir reçeli", "Feigenmarmelade", "Mermelada de higo", "Marmellata di fichi", "Confiture de figues", "无花果酱", "Инжирный джем", "مربى التين") },
            { "Grape jam", T("Grape jam", "Üzüm reçeli", "Traubenmarmelade", "Mermelada de uva", "Marmellata d'uva", "Confiture de raisin", "葡萄酱", "Виноградный джем", "مربى العنب") },
            { "Marmalade", T("Marmalade", "Marmelat", "Marmelade", "Mermelada de cítricos", "Marmellata di agrumi", "Marmelade", "果酱", "Мармелад", "مربى الحمضيات") },
            { "Tamarind paste", T("Tamarind paste", "Demirhindi ezmesi", "Tamarindenpaste", "Pasta de tamarindo", "Pasta di tamarindo", "Pâte de tamarin", "罗望子酱", "Тамариндовая паста", "معجون التمر الهندي") },
            { "Curry paste", T("Curry paste", "Köri ezmesi", "Currypaste", "Pasta de curry", "Pasta di curry", "Pâte de curry", "咖喱酱", "Карри-паста", "معجون الكاري") },
            { "Thai red curry paste", T("Thai red curry paste", "Tay kırmızı köri ezmesi", "Thai-Rotcurrypaste", "Pasta de curry rojo tailandés", "Pasta di curry rosso thailandese", "Pâte de curry rouge thaï", "泰国红咖喱酱", "Тайская красная карри-паста", "معجون الكاري الأحمر التايلاندي") },
            { "Green curry paste", T("Green curry paste", "Yeşil köri ezmesi", "Grüne Currypaste", "Pasta de curry verde", "Pasta di curry verde", "Pâte de curry vert", "绿咖喱酱", "Зеленая карри-паста", "معجون الكاري الأخضر") },
            { "Massaman curry", T("Massaman curry", "Massaman köri", "Massaman-Curry", "Curry massaman", "Curry massaman", "Curry Massaman", "马萨曼咖喱", "Массаман карри", "كاري ماسامان") },
            { "Wasabi", T("Wasabi", "Wasabi", "Wasabi", "Wasabi", "Wasabi", "Wasabi", "芥末", "Васаби", "وسابي") },
            { "Horseradish sauce", T("Horseradish sauce", "Bayır turpu sosu", "Meerrettichsauce", "Salsa de rábano picante", "Salsa di rafano", "Sauce au raifort", "辣根酱", "Соус хрен", "صلصة الفجل الحار") },
            { "Miso paste", T("Miso paste", "Miso ezmesi", "Misopaste", "Pasta de miso", "Pasta di miso", "Pâte de miso", "味噌酱", "Мисо-паста", "معجون الميسو") },
        });

        // --- Noodles and Specialty Products ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Noodles", T("Noodles", "Noodle", "Nudeln", "Fideos", "Noodles", "Nouilles", "面条", "Лапша", "نودلز") },
            { "Ramen noodles", T("Ramen noodles", "Ramen noodle", "Ramen-Nudeln", "Fideos ramen", "Noodles ramen", "Nouilles ramen", "拉面", "Рамен", "نودلز الرامن") },
            { "Udon noodles", T("Udon noodles", "Udon noodle", "Udon-Nudeln", "Fideos udon", "Noodles udon", "Nouilles udon", "乌冬面", "Удон", "نودلز الأودون") },
            { "Rice noodles", T("Rice noodles", "Pirinç noodle", "Reisnudeln", "Fideos de arroz", "Noodles di riso", "Nouilles de riz", "米粉", "Рисовая лапша", "نودلز الأرز") },
            { "Egg noodles", T("Egg noodles", "Yumurtalı noodle", "Eiernudeln", "Fideos de huevo", "Noodles all'uovo", "Nouilles aux œufs", "鸡蛋面", "Яичная лапша", "نودلز البيض") },
            { "Glass noodles", T("Glass noodles", "Şeffaf noodle", "Glasnudeln", "Fideos de cristal", "Noodles di vetro", "Vermicelles de soja", "粉丝", "Стеклянная лапша", "نودلز زجاجية") },
            { "Vermicelli", T("Vermicelli", "Tel şehriye", "Vermicelli", "Fideos finos", "Vermicelli", "Vermicelles", "细面条", "Вермишель", "شعيرية") },
            { "Soba noodles", T("Soba noodles", "Soba noodle", "Soba-Nudeln", "Fideos soba", "Noodles soba", "Nouilles soba", "荞麦面", "Соба", "نودلز سوبا") },
            { "Falafel mix", T("Falafel mix", "Falafel karışımı", "Falafel-Mischung", "Mezcla para falafel", "Preparato per falafel", "Mélange à falafel", "沙拉三明治混合物", "Смесь для фалафеля", "خليط الفلافل") },
            { "Paneer", T("Paneer", "Paneer", "Paneer", "Paneer", "Paneer", "Paneer", "印度奶酪", "Панир", "بانير") },
            { "Lard", T("Lard", "Domuz yağı", "Schweineschmalz", "Manteca de cerdo", "Strutto", "Saindoux", "猪油", "Свиное сало", "شحم الخنزير") },
            { "Suet", T("Suet", "Hayvan iç yağı", "Nierenfett", "Grasa de sebo", "Grasso di rognone", "Suif", "板油", "Почечный жир", "شحم صلب") },
            { "Edible flowers", T("Edible flowers", "Yenilebilir çiçekler", "Essbare Blüten", "Flores comestibles", "Fiori commestibili", "Fleurs comestibles", "可食用花", "Съедобные цветы", "أزهار صالحة للأكل") },
            { "Microgreens", T("Microgreens", "Mikro yeşillikler", "Microgreens", "Microvegetales", "Micro-ortaggi", "Micro-pousses", "微型蔬菜", "Микрозелень", "خضروات صغيرة") },
        });

        // --- Seaweed and Wellness ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Seaweed", T("Seaweed", "Deniz yosunu", "Seetang", "Algas marinas", "Alghe", "Algues", "海藻", "Морские водоросли", "أعشاب بحرية") },
            { "Nori", T("Nori", "Nori", "Nori", "Nori", "Nori", "Nori", "海苔", "Нори", "نوري") },
            { "Wakame", T("Wakame", "Wakame", "Wakame", "Wakame", "Wakame", "Wakamé", "裙带菜", "Вакаме", "واكامي") },
            { "Kombu", T("Kombu", "Kombu", "Kombu", "Kombu", "Kombu", "Kombu", "海带", "Комбу", "كومبو") },
            { "Kelp", T("Kelp", "Kelp", "Seetang", "Algas marinas", "Alga marina", "Varech", "海带", "Ламинария", "عشب البحر") },
            { "Spirulina", T("Spirulina", "Spirulina", "Spirulina", "Espirulina", "Spirulina", "Spiruline", "螺旋藻", "Спирулина", "سبيرولينا") },
            { "Matcha powder", T("Matcha powder", "Matcha tozu", "Matcha-Pulver", "Matcha en polvo", "Tè matcha in polvere", "Poudre de matcha", "抹茶粉", "Матча-порошок", "مسحوق الماتشا") },
        });

        // --- Confectionery and Breakfast ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Marzipan", T("Marzipan", "Marzipan", "Marzipan", "Mazapán", "Marzapane", "Massepain", "杏仁糖", "Марципан", "مرزبان") },
            { "Fondant", T("Fondant", "Şeker hamuru", "Fondant", "Fondant", "Fondente", "Fondant", "翻糖", "Фондант", "عجينة السكر") },
            { "Icing", T("Icing", "Buzlanma", "Zuckerguss", "Glaseado", "Ghiaccia", "Glaçage", "糖衣", "Глазурь", "تغطية") },
            { "Whipped cream", T("Whipped cream", "Krem şanti", "Schlagsahne", "Crema batida", "Panna montata", "Crème fouettée", "生奶油", "Взбитые сливки", "كريمة مخفوقة") },
            { "Strawberry syrup", T("Strawberry syrup", "Çilek şurubu", "Erdbeersirup", "Sirope de fresa", "Sciroppo di fragole", "Sirop de fraise", "草莓糖浆", "Клубничный сироп", "شراب الفراولة") },
            { "Chocolate syrup", T("Chocolate syrup", "Çikolata şurubu", "Schokoladensirup", "Sirope de chocolate", "Sciroppo di cioccolato", "Sirop de chocolat", "巧克力糖浆", "Шоколадный сироп", "شراب الشوكولاتة") },
            { "Banana puree", T("Banana puree", "Muz püresi", "Bananenpüree", "Puré de plátano", "Passato di banana", "Purée de banane", "香蕉泥", "Банановое пюре", "هريس الموز") },
            { "Mango puree", T("Mango puree", "Mango püresi", "Mangopüree", "Puré de mango", "Passato di mango", "Purée de mangue", "芒果泥", "Манговое пюре", "هريس المانجو") },
            { "Raspberry puree", T("Raspberry puree", "Ahududu püresi", "Himbeerpüree", "Puré de frambuesa", "Passato di lamponi", "Purée de framboise", "覆盆子泥", "Малиновое пюре", "هريس التوت") },
            { "Passionfruit puree", T("Passionfruit puree", "Çarkıfelek püresi", "Passionsfruchtpüree", "Puré de maracuyá", "Passato di frutto della passione", "Purée de fruit de la passion", "百香果泥", "Пюре маракуйи", "هريس الباشن فروت") },
            { "Corn flakes", T("Corn flakes", "Mısır gevreği", "Cornflakes", "Copos de maíz", "Fiocchi di mais", "Flocons de maïs", "玉米片", "Кукурузные хлопья", "رقائق الذرة") },
            { "Bran flakes", T("Bran flakes", "Kepekli gevrek", "Kleieflocken", "Copos de salvado", "Fiocchi di crusca", "Flocons de son", "麦麸片", "Отруби", "رقائق النخالة") },
            { "Granola", T("Granola", "Granola", "Müsli", "Granola", "Granola", "Granola", "格兰诺拉麦片", "Гранола", "جرانولا") },
            { "Muesli", T("Muesli", "Müsli", "Müsli", "Muesli", "Muesli", "Muesli", "什锦果麦", "Мюсли", "موسلي") },
        });

        // --- Supplements and Extracts ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Protein powder", T("Protein powder", "Protein tozu", "Proteinpulver", "Proteína en polvo", "Proteine in polvere", "Poudre de protéines", "蛋白粉", "Протеиновый порошок", "مسحوق البروتين") },
            { "Whey protein", T("Whey protein", "Peynir altı suyu proteini", "Molkenprotein", "Proteína de suero", "Proteine del siero di latte", "Protéine de lactosérum", "乳清蛋白", "Сывороточный протеин", "بروتين مصل اللبن") },
            { "Pea protein", T("Pea protein", "Bezelye proteini", "Erbsenprotein", "Proteína de guisante", "Proteine di piselli", "Protéine de pois", "豌豆蛋白", "Гороховый протеин", "بروتين البازلاء") },
            { "Isolate protein", T("Isolate protein", "İzole protein", "Isolatprotein", "Proteína aislada", "Proteine isolate", "Protéine isolée", "分离蛋白", "Изолят протеина", "بروتين معزول") },
            { "Yeast extract", T("Yeast extract", "Maya özü", "Hefeextrakt", "Extracto de levadura", "Estratto di lievito", "Extrait de levure", "酵母提取物", "Дрожжевой экстракт", "خلاصة الخميرة") },
            { "Malt extract", T("Malt extract", "Malt özü", "Malzextrakt", "Extracto de malta", "Estratto di malto", "Extrait de malt", "麦芽精", "Солодовый экстракт", "خلاصة الشعير") },
        });

        // --- Alcoholic and Specialty Liquids ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Beer", T("Beer", "Bira", "Bier", "Cerveza", "Birra", "Bière", "啤酒", "Пиво", "بيرة") },
            { "Cider", T("Cider", "Elma şarabı", "Apfelwein", "Sidra", "Sidro", "Cidre", "苹果酒", "Сидр", "عصير التفاح المخمر") },
            { "Red wine", T("Red wine", "Kırmızı şarap", "Rotwein", "Vino tinto", "Vino rosso", "Vin rouge", "红酒", "Красное вино", "نبيذ أحمر") },
            { "White wine", T("White wine", "Beyaz şarap", "Weißwein", "Vino blanco", "Vino bianco", "Vin blanc", "白酒", "Белое вино", "نبيذ أبيض") },
            { "Cooking wine", T("Cooking wine", "Pişirme şarabı", "Kochwein", "Vino de cocina", "Vino da cucina", "Vin de cuisine", "料酒", "Кулинарное вино", "نبيذ الطبخ") },
            { "Sherry", T("Sherry", "Şeri", "Sherry", "Jerez", "Sherry", "Xérès", "雪利酒", "Херес", "شيري") },
            { "Mirin", T("Mirin", "Mirin", "Mirin", "Mirin", "Mirin", "Mirin", "味醂", "Мирин", "ميرين") },
        });

        // --- Wrappers and Regional Ingredients ---
        AddIngredients(ingredients, new Dictionary<string, Dictionary<string, string>>
        {
            { "Vine leaves", T("Vine leaves", "Asma yaprağı", "Weinblätter", "Hojas de parra", "Foglie di vite", "Feuilles de vigne", "葡萄叶", "Виноградные листья", "أوراق العنب") },
            { "Grape leaves", T("Grape leaves", "Asma yaprağı", "Weinblätter", "Hojas de parra", "Foglie di vite", "Feuilles de vigne", "葡萄叶", "Виноградные листья", "أوراق العنب") },
            { "Kibbeh mix", T("Kibbeh mix", "İçli köfte harcı", "Kibbeh-Mischung", "Mezcla para kibbeh", "Impasto per kibbeh", "Mélange à kebbé", "肉丸混合物", "Смесь для киббе", "خليط كبة") },
            { "Fatty lamb mince", T("Fatty lamb mince", "Yağlı kuzu kıyması", "Fettes Lammhackfleisch", "Carne picada de cordero con grasa", "Macinato d'agnello grasso", "Haché d'agneau gras", "肥羊肉末", "Жирный бараний фарш", "لحم ضأن مفروم دهني") },
            { "Beef tenderloin cubes", T("Beef tenderloin cubes", "Dana bonfile küpleri", "Rinderfiletwürfel", "Cubos de solomillo de res", "Cubetti di filetto di manzo", "Dés de filet de bœuf", "牛里脊块", "Кубики говяжьей вырезки", "مكعبات لحم المتن البقري") },
            { "Shank bone", T("Shank bone", "İncik kemiği", "Haxenknochen", "Hueso de caña", "Osso dello stinco", "Os de jarret", "胫骨", "Кость голяшки", "عظم الساق") },
            { "Sujuk slices", T("Sujuk slices", "Sucuk dilimleri", "Sucuk-Scheiben", "Rodajas de sujuk", "Fette di sucuk", "Tranches de sucuk", "土耳其香肠片", "Ломтики суджука", "شرائح السجق") },
            { "Roasted peppers", T("Roasted peppers", "Kavrulmuş biberler", "Geröstete Paprika", "Pimientos asados", "Peperoni arrostiti", "Poivrons rôtis", "烤辣椒", "Жареный перец", "فلفل مشوي") },
            { "Charred onions", T("Charred onions", "Kömürlenmiş soğan", "Verkohlte Zwiebeln", "Cebollas carbonizadas", "Cipolle carbonizzate", "Oignons carbonisés", "烤焦的洋葱", "Обугленный лук", "بصل متفحم") },
            { "Smoked paprika paste", T("Smoked paprika paste", "İsli biber salçası", "Geräucherte Paprikapaste", "Pasta de pimentón ahumado", "Pasta di paprica affumicata", "Pâte de paprika fumé", "烟熏辣椒粉酱", "Копченая паприка паста", "معجون بابريكا مدخن") },
            { "Caramelized onion", T("Caramelized onion", "Karamelize soğan", "Karamellisierte Zwiebeln", "Cebolla caramelizada", "Cipolla caramellata", "Oignon caramélisé", "焦糖洋葱", "Карамелизированный лук", "بصل مكرمل") },
            { "Roasted garlic", T("Roasted garlic", "Kavrulmuş sarımsak", "Gerösteter Knoblauch", "Ajo asado", "Aglio arrostito", "Ail rôti", "烤大蒜", "Жареный чеснок", "ثوم مشوي") },
            { "Black lime", T("Black lime", "Kara limon", "Schwarze Limette", "Lima negra", "Lime nero", "Citron noir", "黑柠檬", "Черный лайм", "ليمون أسود") },
            { "Dried lemon", T("Dried lemon", "Kuru limon", "Getrocknete Zitrone", "Limón seco", "Limone secco", "Citron séché", "干柠檬", "Сушеный лимон", "ليمون مجفف") },
            { "Sumaghiya mix", T("Sumaghiya mix", "Sumaklı karışım", "Sumaghiya-Mischung", "Mezcla Sumaghiya", "Impasto Sumaghiya", "Mélange Sumaghiya", "苏麦基亚混合物", "Смесь Сумагия", "خليط السماقية") },
            { "Kebab mix", T("Kebab mix", "Kebap harcı", "Kebab-Mischung", "Mezcla para kebab", "Impasto per kebab", "Mélange à kebab", "烤肉串混合物", "Смесь для кебаба", "خليط الكباب") },
            { "Lahmacun topping mix", T("Lahmacun topping mix", "Lahmacun üstü karışımı", "Lahmacun-Belagmischung", "Mezcla de cobertura para lahmacun", "Topping per lahmacun", "Garniture à lahmacun", "土耳其披萨馅料", "Начинка для лахмаджуна", "خليط حشوة اللحم بعجين") },
        });

        // --- Final Commit ---
        await context.GlobalIngredients.AddRangeAsync(ingredients);
        await context.SaveChangesAsync();

        logger.LogInformation($"Successfully seeded {ingredients.Count} global ingredients");
    }

    private static void AddIngredients(List<GlobalIngredient> list, Dictionary<string, Dictionary<string, string>> ingredients)
    {
        foreach (var item in ingredients)
        {
            list.Add(CreateIngredient(item.Key, item.Value));
        }
    }

    private static GlobalIngredient CreateIngredient(string defaultName, Dictionary<string, string> translations)
    {
        return new GlobalIngredient
        {
            DefaultName = defaultName,
            IsActive = true,
            CreatedBy = "System",
            Translations = translations.Select(t => new GlobalIngredientTranslation
            {
                LanguageCode = t.Key,
                Name = t.Value,
                CreatedBy = "System"
            }).ToList()
        };
    }
}
