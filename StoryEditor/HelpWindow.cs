using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StoryEditor2.Wpf;

public sealed class HelpWindow : Window
{
    private sealed record HelpTopic(string Title, string Content, string[] Keywords);

    private readonly List<HelpTopic> _topics = CreateTopics();
    private readonly ListBox _topicList = new();
    private readonly TextBox _searchBox = new();
    private readonly TextBox _contentBox = new();
    private readonly Button _languageButton = new();
    private readonly TextBlock _footerText = new();
    private bool _russian;

    public HelpWindow()
    {
        Title = "StoryEditor — Osiris Help";
        Width = 1120;
        Height = 780;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#FF1E1E1E");
        Foreground = Brush("#FFE0E0E0");

        var root = new DockPanel { Margin = new Thickness(12) };
        var header = new DockPanel { Margin = new Thickness(2, 0, 0, 10) };
        var heading = new TextBlock
        {
            Text = "OSIRIS LANGUAGE REFERENCE",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#FF9CDCFE"),
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(heading);
        _languageButton.Content = "Русский";
        _languageButton.Padding = new Thickness(12, 4, 12, 4);
        _languageButton.HorizontalAlignment = HorizontalAlignment.Right;
        _languageButton.Click += (_, _) =>
        {
            _russian = !_russian;
            _languageButton.Content = _russian ? "English" : "Русский";
            heading.Text = _russian ? "СПРАВКА ПО ЯЗЫКУ OSIRIS" : "OSIRIS LANGUAGE REFERENCE";
            _searchBox.ToolTip = _russian ? "Поиск по разделам справки" : "Search help topics";
            _footerText.Text = _russian
                ? "Справочник по синтаксису Osiris в Divine Divinity. Некоторые особенности движка восстановлены по Story игры и требуют дальнейшей проверки."
                : "Reference for the Osiris syntax used by Divine Divinity. Some engine-specific behavior is inferred from the game's Story and may require further verification.";
            RefreshTopics();
        };
        header.Children.Add(_languageButton);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _footerText.Text = "Reference for the Osiris syntax used by Divine Divinity. Some engine-specific behavior is inferred from the game's Story and may require further verification.";
        _footerText.TextWrapping = TextWrapping.Wrap;
        _footerText.Foreground = Brush("#FF999999");
        _footerText.Margin = new Thickness(2, 10, 2, 0);
        DockPanel.SetDock(_footerText, Dock.Bottom);
        root.Children.Add(_footerText);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250), MinWidth = 190 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 400 });

        var navigation = new DockPanel { Background = Brush("#FF252526"), Margin = new Thickness(0, 0, 5, 0) };
        _searchBox.Margin = new Thickness(7);
        _searchBox.Padding = new Thickness(7, 5, 7, 5);
        _searchBox.Background = Brush("#FF2D2D30");
        _searchBox.Foreground = Brush("#FFE0E0E0");
        _searchBox.BorderBrush = Brush("#FF55555A");
        _searchBox.CaretBrush = Brushes.White;
        _searchBox.ToolTip = "Search help topics";
        _searchBox.TextChanged += (_, _) => RefreshTopics();
        DockPanel.SetDock(_searchBox, Dock.Top);
        navigation.Children.Add(_searchBox);

        _topicList.Background = Brush("#FF252526");
        _topicList.Foreground = Brush("#FFE0E0E0");
        _topicList.BorderThickness = new Thickness(0);
        _topicList.SelectionChanged += (_, _) => ShowSelectedTopic();
        navigation.Children.Add(_topicList);
        Grid.SetColumn(navigation, 0);
        body.Children.Add(navigation);

        var splitter = new GridSplitter
        {
            Width = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brush("#FF3F3F46")
        };
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);

        _contentBox.IsReadOnly = true;
        _contentBox.AcceptsReturn = true;
        _contentBox.TextWrapping = TextWrapping.Wrap;
        _contentBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _contentBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _contentBox.FontFamily = new FontFamily("Consolas");
        _contentBox.FontSize = 13;
        _contentBox.Padding = new Thickness(12);
        _contentBox.Background = Brush("#FF202020");
        _contentBox.Foreground = Brush("#FFD4D4D4");
        _contentBox.BorderThickness = new Thickness(0);
        Grid.SetColumn(_contentBox, 2);
        body.Children.Add(_contentBox);

        root.Children.Add(body);
        Content = root;

        RefreshTopics();
        if (_topicList.Items.Count > 0)
            _topicList.SelectedIndex = 0;
    }

    private void RefreshTopics()
    {
        string query = _searchBox.Text.Trim();
        var matches = _topics.Where(topic =>
            query.Length == 0 ||
            topic.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            topic.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            GetRussianTitle(topic.Title).Contains(query, StringComparison.OrdinalIgnoreCase) ||
            GetRussianContent(topic.Title).Contains(query, StringComparison.OrdinalIgnoreCase) ||
            topic.Keywords.Any(keyword => keyword.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        string? selectedTitle = _topicList.SelectedItem as string;
        _topicList.ItemsSource = matches.Select(topic => _russian
            ? GetRussianTitle(topic.Title) : topic.Title).ToArray();
        int selected = Array.FindIndex(matches, topic =>
            (_russian ? GetRussianTitle(topic.Title) : topic.Title) == selectedTitle);
        _topicList.SelectedIndex = selected >= 0 ? selected : matches.Length > 0 ? 0 : -1;
        if (matches.Length == 0)
            _contentBox.Text = _russian
                ? "По вашему запросу ничего не найдено."
                : "No help topics match the search.";
    }

    private void ShowSelectedTopic()
    {
        if (_topicList.SelectedItem is not string selected)
            return;

        var topic = _topics.FirstOrDefault(item =>
            (_russian ? GetRussianTitle(item.Title) : item.Title) == selected);
        if (topic is not null)
            _contentBox.Text = _russian ? GetRussianContent(topic.Title) : topic.Content;
    }

    private static string GetRussianTitle(string title) => title switch
    {
        "Language overview" => "Общие сведения",
        "Story file structure" => "Структура Story-файла",
        "IF rules" => "Правила IF",
        "PROC procedures" => "Процедуры PROC",
        "Conditions and actions" => "Условия и действия",
        "Variables and types" => "Переменные и типы",
        "Expressions and punctuation" => "Выражения и пунктуация",
        "Comments and formatting" => "Комментарии и форматирование",
        "Editor workflow" => "Работа в StoryEditor",
        "References" => "References — перекрёстные ссылки",
        "Goal Links" => "Goal Links — связи между Goal",
        "Symbol Links" => "Symbol Links — связи символов",
        "Procedure Analysis" => "Procedure Analysis — анализ процедур",
        "Lifecycle action editor" => "Контекстное меню действий INIT и EXIT",
        "Grammar validation" => "Проверка грамматики Osiris",
        _ => title
    };

    private static string GetRussianContent(string title) => title switch
    {
        "Language overview" => """
OSIRIS В DIVINE DIVINITY

Osiris — язык сценариев, основанный на правилах. Файл Story содержит объявления типов и объектов, а также блоки Goal. Внутри Goal находятся секции жизненного цикла и база знаний (KB).

Основные формы правил — IF и PROC. Правило состоит из части условий, разделителя THEN и части действий.

Главное:
• Условия соединяются операторами AND и AND NOT. Точки с запятой между условиями не ставятся.
• Каждое действие завершается точкой с запятой.
• Вызовы функций записываются со скобками, аргументы разделяются запятыми.
• Рекомендуется сохранять исходное написание имён из Story игры.

Пример:

IF
CharacterIsInTrigger(NPC_Hero, TRIGGER_Example)
THEN
SetStatus(STATUS_Example, 1);
""",
        "Story file structure" => """
СТРУКТУРА STORY-ФАЙЛА

Файл Story содержит объявления типов, объектов и блоки Goal.

Примеры объявлений:

type { NPC, 4 }
type { OBJECT, 5 }

object { NPC_Hero, 4, ( 4, 0, 0, 0 ) }
object { OBJECT_Example, 5, ( 5, 100, 0, 0 ) }

Goal имеет числовой идентификатор и тело:

Goal (1)
{
    INIT
    {
        // Действия при инициализации
    }

    KB
    {
        // Правила IF и PROC
    }

    EXIT
    {
        // Действия при завершении
    }
}

INIT выполняется при инициализации Goal, KB содержит правила, EXIT — действия при завершении. Числовые идентификаторы типов и формат записей object относятся к структуре данных игры; не изменяйте их без понимания соответствующих таблиц.
""",
        "IF rules" => """
ПРАВИЛА IF

Правило IF описывает условия, при выполнении которых запускаются действия.

Общая форма:

IF
ConditionA(...)
AND
ConditionB(...)
AND NOT
ConditionC(...)
THEN
ActionA();
ActionB();

Первое условие также может быть отрицательным:

IF
NOT ConditionA(...)
THEN
ActionA();

Связки:
• AND — все соединённые условия должны выполняться.
• AND NOT — следующее условие должно быть ложным.
• NOT перед первым условием — отрицание первого условия.

Условия не завершаются точкой с запятой. Действия после THEN завершаются точкой с запятой.
""",
        "PROC procedures" => """
ПРОЦЕДУРЫ PROC

Правило PROC объявляет пользовательскую процедуру Story. Имя и типизированные параметры образуют сигнатуру процедуры.

Пример объявления:

PROC
UpdateTime((INTEGER)_Hours, (INTEGER)_Minutes)
AND
Time(_Hour, _Minute, _Second)
THEN
NOT Time(_Hour, _Minute, _Second);
SetTime(_Hours, _Minutes);

Вызов процедуры из другого правила:

IF
SomeEvent(...)
THEN
UpdateTime(10, 30);

Параметр объявляется в форме (ТИП)_ИмяПеременной. Типы и порядок параметров при вызове должны соответствовать сигнатуре процедуры.

Вызов процедуры в секции действий завершается точкой с запятой, как и остальные действия. После самой сигнатуры PROC точка с запятой не ставится.
""",
        "Conditions and actions" => """
УСЛОВИЯ И ДЕЙСТВИЯ

Условия располагаются между IF / PROC и THEN. Они проверяют состояние игры или факт наступления события.

Действия располагаются после THEN. Они изменяют состояние игры, вызывают поведение движка или пользовательские процедуры.

Пример:

IF
ItemCount(NPC_Hero, ITEM_Gold, _Gold)
AND
_Gold >= 100
THEN
UseItem(NPC_Hero, ITEM_Gold);

Доступные условия и действия определяются API движка и содержимым Story игры. Используйте Symbol Explorer и автодополнение, чтобы посмотреть известные имена и сигнатуры.

В оригинальном Story встречаются и отрицательные действия:

THEN
NOT SomeAction(...);

Не следует считать, что любой вызов API допустим одновременно в секции условий и в секции действий.
""",
        "Variables and types" => """
ПЕРЕМЕННЫЕ И ТИПЫ

Переменные Osiris обычно начинаются с символа подчёркивания:

_Variable
_Hero
_Count

Типизированные параметры PROC записываются как тип в круглых скобках, затем имя переменной:

(INTEGER)_Count
(STRING)_Text
(REAL)_Value

Примеры значений:

42
3.14
"Some text"
NPC_Hero
OBJECT_Chest

Имена NPC_Hero и OBJECT_Chest — идентификаторы объектов/символов Story, а не строковые литералы.

Точный набор типов зависит от объявлений API и движка. Проверяйте сигнатуру в Symbol Explorer, не угадывайте тип параметра.
""",
        "Expressions and punctuation" => """
ВЫРАЖЕНИЯ И ПУНКТУАЦИЯ

Основные знаки в исходном коде Story:

( )  Вызовы функций и группировка выражений
,    Разделение аргументов
;    Завершение действий и операторов верхнего уровня
{ }  Границы объявлений и секций Goal
" "  Строковые литералы

В выражениях Story встречаются операторы сравнения:

=   равно
!=  не равно
<   меньше
>   больше
<=  меньше или равно
>=  больше или равно

Поддержка арифметических и логических выражений зависит от контекста. Во многих оригинальных скриптах вычисления выполняются вызовами API, например IntegerSum, IntegerSubtract и IntegerProduct.

Примеры:

IntegerSum(_A, _B, _Result)
_Gold >= 100

Не оборачивайте правила IF/PROC в фигурные скобки. Правила ограничиваются ключевыми словами и внешним блоком KB.
""",
        "Comments and formatting" => """
КОММЕНТАРИИ И ФОРМАТИРОВАНИЕ

Лексер редактора распознаёт однострочные и блочные комментарии:

// Однострочный комментарий

/*
   Многострочный комментарий
*/

Комментарии подходят для пояснения логики. Размещайте их между конструкциями, если синтаксис комментариев внутри аргументов не подтверждён.

ФОРМАТИРОВАНИЕ

Переносы строк и отступы повышают читаемость, но не заменяют ключевые слова и знаки пунктуации.

Рекомендуемый стиль:

IF
    Event(...)
AND
    Condition(...)
THEN
    ActionOne();
    ActionTwo();

Перед крупными изменениями используйте подсветку синтаксиса и проверку.
""",
        "Editor workflow" => """
РАБОТА В STORYEDITOR

• Откройте Story-файл или выполните его декомпиляцию.
• Выберите Goal в дереве слева.
• Редактируйте INIT, KB и EXIT в соответствующих панелях.
• В INIT и EXIT щёлкните правой кнопкой мыши и используйте контекстное меню для вставки API-вызовов, Story-процедур и встроенных действий.
• Дважды щёлкните правило в списке, чтобы открыть отдельное окно его редактирования.
• Используйте контекстное меню для вставки API-функций, процедур Story и ключевых слов Osiris.
• Используйте автодополнение при вводе имени функции или процедуры.
• Откройте Symbol Explorer для просмотра сигнатур, определений и мест использования.
• Используйте References и Goal Links для перехода между связанными символами и Goal.
• Проверьте и примените изменения правила перед сохранением.
• Сохраните исходный код в отдельный файл, затем соберите игровой Story через функцию Build.

Автодополнение помогает при вводе, но не гарантирует допустимость вызова в текущей секции условий или действий. Проверяйте сигнатуру и примеры использования в оригинальном Story.
""",
        "References" => """
REFERENCES — ПЕРЕКРЁСТНЫЕ ССЫЛКИ

Эта вкладка индексирует содержимое всех Goal и позволяет искать упоминания по всему Story-файлу.

Как пользоваться:
1. Откройте вкладку References.
2. Введите имя функции, объекта, переменной или фрагмент исходного текста в строку поиска.
3. Просмотрите результаты. В строке результата указаны Goal, заголовок Goal, тип правила (IF/PROC), найденные вызовы и фрагмент условия или действия.
4. Дважды щёлкните результат — редактор перейдёт к соответствующему Goal и выделит правило в списке.

Поиск выполняется по индексируемому тексту правил KB, включая вызовы функций и имена объектов. Это инструмент поиска по исходному коду, а не доказательство семантической зависимости между Goal.

Если вы изменили KB текущего Goal, список перекрёстных ссылок обновляется по текущему тексту редактора. Для поиска по ещё не применённым изменениям в других Goal сначала примените эти изменения.
""",
        "Goal Links" => """
GOAL LINKS — СВЯЗИ МЕЖДУ GOAL

Вкладка показывает связи выбранного Goal с другими Goal. Сначала выберите Goal в дереве слева, затем откройте Goal Links.

Что отображается:
• API-confirmed references — связи, найденные по известным операциям Osiris API, работающим с состоянием Goal (активация, завершение, проверка состояния и т. п.).
• Story PROC_CompleteGoal handlers — вызовы, сопоставленные с реальными обработчиками PROC_CompleteGoal в Story.
• Title-only candidates — предположительные совпадения по названию Goal. Это кандидаты, а не подтверждённые связи.
• Shared-call heuristics — Goal, в которых встречаются те же имена вызовов. Совпадение вызовов само по себе не доказывает зависимость.

Левая часть содержит список связанных Goal. На графе выбранный Goal находится в центре, а найденные связи показаны вокруг него. Цвет и тип линии различают подтверждённые API-связи, найденные обработчики процедур, кандидатов по названию и эвристические совпадения.

Выберите Goal в списке, чтобы увидеть статистику и доказательства связи. В нижнем списке Evidence показаны конкретные вызовы, направление связи и место в исходном коде. Дважды щёлкните доказательство, чтобы открыть исходный Goal и перейти к точному месту в KB, INIT или EXIT.

Важно: ориентируйтесь на тип доказательства. Эвристические и title-only совпадения нельзя трактовать как установленную зависимость.
""",
        "Symbol Links" => """
SYMBOL LINKS / SYMBOL EXPLORER — СВЯЗИ СИМВОЛОВ

Эта вкладка предназначена для изучения символов Osiris и их использования в Story. Она помогает понять, что означает вызов, какие параметры он принимает и где встречается.

Как пользоваться:
1. Выберите категорию в фильтре: Events, Queries, Calls, System queries, System calls, Objects, Types, Story procedures, Game database symbols или Unresolved calls.
2. Введите часть имени или сигнатуры в поле поиска.
3. Выберите символ в левом списке.
4. В верхней правой панели изучите сигнатуру и сведения о символе.
5. В нижней правой панели All usages просмотрите места использования символа.
6. Дважды щёлкните использование, чтобы открыть соответствующий Goal и перейти к правилу. Для Story procedure двойной щелчок по самому символу открывает его объявление.

Категории помогают отделить события и запросы от вызываемых действий, пользовательских процедур и игровых символов. Unresolved calls полезен для поиска вызовов, которые редактор не смог сопоставить с известным API или объявленной процедурой.

Список использований — индекс найденных в загруженном Story вызовов, а не полный список всех возможных обращений из внешних файлов игры. Если символ не найден, проверьте написание, категорию и то, что нужный Story загружен.
""",
        "Grammar validation" => """
ПРОВЕРКА ГРАММАТИКИ OSIRIS

Откройте вкладку Grammar в основном окне StoryEditor. Проверка выполняется автоматически после паузы при редактировании и анализирует весь текущий Story, включая несохранённые изменения.

Что проверяется на текущем этапе:
• незакрытые строковые литералы и блочные комментарии;
• баланс круглых и квадратных скобок, а также структура фигурных блоков;
• наличие обязательных частей IF/PROC, разделителя THEN и корректного списка действий;
• синтаксическая форма условий: вызов предиката или сравнение значений;
• синтаксис действий, завершающая точка с запятой и форма PROC-сигнатур;
• допустимая роль известных API-функций: условие или действие;
• структура секций INIT и EXIT.

Каждая ошибка содержит номер строки, столбец и код диагностики. Если ошибок нет, вкладка показывает сводку по количеству разобранных правил, условий и действий.

ВАЖНЫЕ ОГРАНИЧЕНИЯ

Проверка развивается поэтапно. На текущем этапе она не является полной проверкой типов всех аргументов KB и не доказывает семантическую корректность сценария. Неизвестные имена разрешены, поскольку Story содержит пользовательские факты и процедуры. Проверка также не заменяет компилятор оригинальной OsirisDLL.

Для окончательной проверки перед использованием в игре выполните Build: исходный компилятор OsirisDLL остаётся последней инстанцией проверки совместимости с движком.
""",
        "Lifecycle action editor" => """
ДЕЙСТВИЯ INIT И EXIT — КОНТЕКСТНОЕ МЕНЮ

В секциях INIT и EXIT все вставки выполняются непосредственно в основном редакторе через контекстное меню (правая кнопка мыши).

Доступны:
• Insert API action — список API-вызовов типа Call и SystemCall. При выборе вставляется шаблон вызова с круглыми скобками и завершающей точкой с запятой.
• Insert Story procedure — список процедур, объявленных в загруженном Story. Вставка также завершается точкой с запятой.
• GoalCompleted; — встроенное действие завершения Goal.
• Goal control directive (!) — вставляет символ !, после которого можно указать номер Goal.
• NOT action prefix — вставляет NOT перед вызовом.

Автодополнение при вводе имени в INIT/EXIT ограничено вызываемыми API-действиями и Story-процедурами; события и запросы не предлагаются как действия. Вставка не заменяет проверку аргументов и порядка выполнения.

Точка с запятой обязательна в конце каждого действия. После вставки отредактируйте аргументы и сохраните Story обычной командой Save.
""",
        "Procedure Analysis" => """
PROCEDURE ANALYSIS — АНАЛИЗ ПРОЦЕДУР

Вкладка анализирует пользовательские Story-процедуры и места их вызова. Она объединяет сведения из KB, INIT и EXIT.

В верхней строке показана общая статистика:
• Procedures — количество найденных процедур.
• No indexed call sites — процедуры, для которых не найдено ни одного индексированного вызова.
• Multiple handlers — имена процедур, для которых найдено несколько правил-обработчиков.
• With outgoing calls — обработчики, которые сами вызывают другие процедуры.
• INIT calls / EXIT calls — количество вызовов процедур в секциях инициализации и завершения.
• Unresolved targets — имена вызываемых процедур, для которых не найден соответствующий обработчик.

В левом списке каждая строка содержит имя процедуры, статус и счётчики:
H — количество правил-обработчиков (handler rules);
C — количество мест вызова (call sites);
KB, I, E — вызовы из KB, INIT и EXIT;
-> — количество различных процедур, вызываемых из тела обработчика.

Введите имя или часть имени в поле поиска, чтобы отфильтровать список. Выберите процедуру — справа появится сводка: обработчики, места вызова, распределение вызовов по секциям и дерево CALL GRAPH, показывающее цепочку вызовов из тела обработчика. В нижнем списке перечислены конкретные правила и места вызова.

Дважды щёлкните строку обработчика или места вызова, чтобы перейти к исходному коду. Для вызовов из KB открывается соответствующее правило; вызовы из INIT и EXIT открывают нужную секцию и выделяют вызов.

Интерпретация результатов:
• NO CALL SITES — в проиндексированных секциях не найден вызов этой процедуры; это не обязательно означает, что она никогда не вызывается внешним кодом.
• MULTIPLE HANDLERS — найдено более одного PROC-обработчика с таким именем; проверьте все определения.
• CALLS OTHER PROCEDURES — тело обработчика вызывает другие пользовательские процедуры.
• UNRESOLVED TARGETS — в цепочке вызовов есть имена без найденного обработчика.

Анализ строится по текущему загруженному Story и обновляется при изменении индексируемых данных. Это статический анализ исходного кода: он не исполняет Osiris и не доказывает, что конкретная ветвь будет достигнута во время игры.
""",
        _ => title
    };

    private static SolidColorBrush Brush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));

    private static List<HelpTopic> CreateTopics() =>
    [
        new("Language overview", """
OSIRIS IN DIVINE DIVINITY

Osiris is a rule-based scripting language. A Story file is organized into declarations and Goal blocks. Goals contain lifecycle sections and a Knowledge Base (KB).

The main rule forms are IF rules and PROC (procedure) rules. A rule has a condition part, the THEN separator, and an action part.

Important:
• Conditions are connected with AND / AND NOT. Do not add semicolons between conditions.
• Actions are terminated with semicolons.
• Function calls use parentheses. Arguments are separated with commas.
• Names are case-insensitive in the game's Story examples, but preserving the original spelling is recommended.

Example:

IF
CharacterIsInTrigger(NPC_Hero, TRIGGER_Example)
THEN
SetStatus(STATUS_Example, 1);

""", ["basics", "overview", "rule", "story"]),

        new("Story file structure", """
STORY STRUCTURE

A Story file contains type declarations, object declarations and Goal blocks.

Common declaration forms:

type { NPC, 4 }
type { OBJECT, 5 }

object { NPC_Hero, 4, ( 4, 0, 0, 0 ) }
object { OBJECT_Example, 5, ( 5, 100, 0, 0 ) }

A Goal has a numeric identifier and a body:

Goal (1)
{
    INIT
    {
        // Initialization actions
    }

    KB
    {
        // IF and PROC rules
    }

    EXIT
    {
        // Exit actions
    }
}

The exact object type IDs and object records are part of the Story data format. Do not change them unless you know the corresponding game database mapping.

""", ["goal", "type", "object", "INIT", "KB", "EXIT", "braces"]),

        new("IF rules", """
IF RULES

An IF rule describes conditions that must be satisfied before its actions are executed.

General form:

IF
ConditionA(...)
AND
ConditionB(...)
AND NOT
ConditionC(...)
THEN
ActionA();
ActionB();

The first condition may also be negated:

IF
NOT ConditionA(...)
THEN
ActionA();

Connectors:
• AND — all connected conditions must hold.
• AND NOT — the following condition must not hold.
• NOT before the first condition — negates that first condition.

Conditions are not terminated with semicolons. Actions are terminated with semicolons.

""", ["IF", "conditions", "AND", "NOT", "THEN"]),

        new("PROC procedures", """
PROC PROCEDURES

A PROC rule declares a reusable Story procedure. Its name and typed parameters form the procedure signature.

Example declaration:

PROC
UpdateTime((INTEGER)_Hours, (INTEGER)_Minutes)
AND
Time(_Hour, _Minute, _Second)
THEN
NOT Time(_Hour, _Minute, _Second);
SetTime(_Hours, _Minutes);

Call the procedure from another rule like an ordinary function call:

IF
SomeEvent(...)
THEN
UpdateTime(10, 30);

The parameter declaration uses the form (TYPE)_VariableName. The types and parameter order must match the procedure declaration.

A procedure call in the action section ends with a semicolon, just like other actions. Do not add a semicolon to the end of the PROC signature itself.

""", ["PROC", "procedure", "signature", "parameter", "function"]),

        new("Conditions and actions", """
CONDITIONS VS ACTIONS

Conditions appear between IF / PROC and THEN. They query game state or test whether an event/condition is true.

Actions appear after THEN. They change game state, invoke engine behavior or call procedures.

Example:

IF
ItemCount(NPC_Hero, ITEM_Gold, _Gold)
AND
_Gold >= 100
THEN
UseItem(NPC_Hero, ITEM_Gold);

The exact available conditions and actions are defined by the engine API and the game's Story data. Use Symbol Explorer and autocomplete to inspect known symbols and signatures.

Negated actions may appear in the game's Story, for example:

THEN
NOT SomeAction(...);

Do not assume every API call is valid in both condition and action positions.

""", ["condition", "action", "semicolon", "query", "call"]),

        new("Variables and types", """
VARIABLES AND TYPES

Osiris variables are commonly written with a leading underscore:

_Variable
_Hero
_Count

Typed PROC parameters use a type in parentheses followed by the variable:

(INTEGER)_Count
(STRING)_Text
(REAL)_Value

Examples of values:

42
3.14
"Some text"
NPC_Hero
OBJECT_Chest

Identifiers such as NPC_Hero and OBJECT_Chest are Story object/symbol names, not string literals.

The exact set of supported types depends on the engine/API declarations. Check the signature shown by Symbol Explorer rather than guessing a parameter type.

""", ["variable", "INTEGER", "STRING", "REAL", "types"]),

        new("Expressions and punctuation", """
EXPRESSIONS AND PUNCTUATION

Common punctuation used in Story source:

( )  Function calls and grouped expressions
,    Separates function arguments
;    Terminates actions and top-level Story statements
{ }  Delimits declarations and Goal sections
" "  Delimits string literals

Comparison operators found in Story expressions include:

=   equality
!=  not equal
<   less than
>   greater than
<=  less than or equal
>=  greater than or equal

Arithmetic and logical expression support can depend on the context and engine functions. Many calculations in the original Story are performed through API calls such as IntegerSum, IntegerSubtract and IntegerProduct.

Examples:

IntegerSum(_A, _B, _Result)
_Gold >= 100

Do not use C-style braces to wrap IF/PROC rules. Rules are delimited by their keywords and the surrounding KB block.

""", ["operators", "punctuation", "parentheses", "semicolon", "comparison"]),

        new("Comments and formatting", """
COMMENTS

The Story lexer recognizes both single-line and block comments:

// Single-line comment

/*
   Block comment
*/

Comments can be used to explain a rule or temporarily document code. Keep comments outside function argument lists unless the syntax has been verified for that context.

FORMATTING

Line breaks and indentation improve readability but do not replace Osiris keywords or punctuation.

Recommended layout:

IF
    Event(...)
AND
    Condition(...)
THEN
    ActionOne();
    ActionTwo();

Use the editor's syntax highlighting and validation before applying larger edits.

""", ["comment", "format", "indentation", "//", "/* */"]),

        new("Editor workflow", """
WORKING WITH STORYEDITOR

• Open or decompile a Story file.
• Select a Goal in the left tree.
• Edit INIT, KB and EXIT in the corresponding panels.
• In INIT and EXIT, right-click in the editor to insert API actions, Story procedures and built-in action syntax from the context menu.
• Double-click a rule in the rule list to open the dedicated rule editor.
• Use the context menu to insert API functions, Story procedures and Osiris keywords.
• Use autocomplete while typing a function or procedure name.
• Use References to search indexed rules across Goals.
• Use Goal Links to inspect confirmed and heuristic Goal relationships.
• Use Symbol Explorer (Symbol Links) to inspect symbol signatures and usages.
• Use Procedure Analysis to inspect Story procedure handlers, call sites and call chains.
• Validate and apply rule edits before saving.
• Save edited source to a separate file, then build the game Story using the editor's build workflow.

Autocomplete is a convenience, not a guarantee that a call is valid in the current condition/action context. Check the symbol signature and the game's existing usage.
""", ["editor", "workflow", "autocomplete", "symbol explorer"]),

        new("Grammar validation", """
OSIRIS GRAMMAR VALIDATION

Open the Grammar tab in the main StoryEditor window. Validation runs automatically after a short pause while editing and analyzes the current Story, including unsaved changes.

Checks implemented in this first stage:
• unterminated string literals and block comments;
• balanced parentheses and square brackets, plus structural curly-brace blocks;
• required IF/PROC parts, the THEN separator and action-list structure;
• condition syntax as either a predicate call or a value comparison;
• action-call syntax, terminating semicolons and PROC signatures;
• whether known API functions are used in a condition or action position appropriate to their declaration;
• INIT and EXIT section structure.

Each diagnostic includes a source line, column and diagnostic code. When no errors are found, the tab shows counts for parsed rules, conditions and actions.

CURRENT LIMITATIONS

Validation is being expanded incrementally. This stage is not yet a complete type checker for every KB argument and does not prove semantic correctness. Unknown names are allowed because Story files contain user-defined facts and procedures. The validator also does not replace the original OsirisDLL compiler.

Run Build before using a modified Story in the game: the original OsirisDLL compiler remains the final compatibility check.
""", ["grammar", "syntax", "validation", "diagnostics", "errors", "compiler", "IF", "PROC"]),

        new("Lifecycle action editor", """
INIT AND EXIT ACTIONS — CONTEXT MENU

All insertions in INIT and EXIT happen directly in the main editor through its right-click context menu.

Available entries:
• Insert API action — lists API symbols declared as Call or SystemCall. Selecting one inserts a call template with parentheses and a terminating semicolon.
• Insert Story procedure — lists procedures declared in the loaded Story and inserts a call ending in a semicolon.
• GoalCompleted; — built-in Goal-completion action.
• Goal control directive (!) — inserts ! so you can enter the target Goal number.
• NOT action prefix — inserts NOT before an action call.

Autocomplete in INIT/EXIT is limited to callable API actions and Story procedures; events and queries are not offered as actions. Insertion does not replace checking argument values or execution order.

A semicolon is required at the end of every action. Edit the arguments after insertion, then save the Story with the normal Save command.
""", ["INIT", "EXIT", "lifecycle", "actions", "context menu", "insert action"]),

        new("References", """
REFERENCES — CROSS-REFERENCES

This tab indexes content from all Goals and lets you search mentions across the Story source.

How to use it:
1. Open the References tab.
2. Enter a function name, object name, variable or source-text fragment in the search box.
3. Review the results. Each row shows the Goal, Goal title, rule kind (IF/PROC), discovered calls and a compact excerpt from a condition or action.
4. Double-click a result to navigate to that Goal and select the corresponding rule.

The search covers indexed KB rule text, including function calls and object names. It is a source-code search tool, not proof of a semantic dependency between Goals.

When you edit the current Goal's KB, its cross-reference entries are refreshed from the editor text. Apply edits in other Goals before expecting those changes to be reflected in the index.
""", ["references", "cross reference", "search", "usages", "navigation"]),

        new("Goal Links", """
GOAL LINKS — GOAL DEPENDENCIES

This tab shows relationships between the currently selected Goal and other Goals. Select a Goal in the left-hand tree before opening Goal Links.

Relationship types:
• API-confirmed references — links found through known Osiris API operations that act on Goal state, such as activation, completion and state checks.
• Story PROC_CompleteGoal handlers — calls matched to actual PROC_CompleteGoal handlers declared in the Story.
• Title-only candidates — possible matches based on Goal titles. These are candidates, not confirmed links.
• Shared-call heuristics — Goals containing some of the same call names. Shared calls alone do not prove a dependency.

The left pane lists related Goals. The graph places the current Goal in the center and related Goals around it. Line styles and colors distinguish API-confirmed links, resolved procedure handlers, title-based candidates and heuristic overlaps.

Select a related Goal to inspect counts and evidence. The Evidence list shows concrete calls, link direction and source location. Double-click an evidence row to open the source Goal and navigate to the exact location in KB, INIT or EXIT.

Always consider the evidence category. Title-only and heuristic matches must not be treated as established dependencies.
""", ["goal links", "dependencies", "goal", "graph", "evidence"]),

        new("Symbol Links", """
SYMBOL LINKS / SYMBOL EXPLORER

This tab is used to inspect Osiris symbols and their usage in the loaded Story. It helps identify what a call represents, which parameters it accepts and where it appears.

How to use it:
1. Choose a category in the filter: Events, Queries, Calls, System queries, System calls, Objects, Types, Story procedures, Game database symbols or Unresolved calls.
2. Enter part of a symbol name or signature in the search field.
3. Select a symbol in the left-hand list.
4. Inspect its signature and symbol information in the upper-right panel.
5. Review indexed occurrences in the lower-right All usages list.
6. Double-click a usage to navigate to its Goal and rule. Double-clicking a Story procedure opens its declaration.

Categories separate events and queries from callable actions, user-defined procedures and game database symbols. Unresolved calls is useful for finding calls that could not be matched to a known API symbol or declared procedure.

The usage list is an index of calls found in the loaded Story, not a complete list of every possible reference from external game files. If a symbol is missing, check its spelling, selected category and whether the intended Story file is loaded.
""", ["symbol links", "symbol explorer", "symbols", "signature", "usages", "unresolved"]),

        new("Procedure Analysis", """
PROCEDURE ANALYSIS — PROCEDURE CALL OVERVIEW

This tab analyzes user-defined Story procedures and their call sites. It combines information from KB, INIT and EXIT.

The summary line shows:
• Procedures — number of discovered procedures.
• No indexed call sites — procedures with no indexed calls.
• Multiple handlers — procedure names with more than one handler rule.
• With outgoing calls — handlers that call other procedures.
• INIT calls / EXIT calls — procedure calls found in initialization and exit sections.
• Unresolved targets — called procedure names for which no matching handler was found.

Each row in the left list shows a procedure name, status and counters:
H — number of handler rules;
C — number of call sites;
KB, I, E — calls from KB, INIT and EXIT;
-> — number of distinct procedures called from the handler body.

Use the search field to filter by procedure name. Select a procedure to inspect its summary, handlers, call sites, section counts and CALL GRAPH on the right. The graph represents the chain of procedure calls originating from the handler body. The lower list contains individual handler and call-site rules.

Double-click a handler or call-site row to navigate to its source. KB calls open the corresponding rule; INIT and EXIT calls open the relevant section and select the call.

Interpreting results:
• NO CALL SITES — no call was found in the indexed sections; this does not prove the procedure is never called by external code.
• MULTIPLE HANDLERS — more than one PROC handler with this name was found; inspect all definitions.
• CALLS OTHER PROCEDURES — the handler body calls other user-defined procedures.
• UNRESOLVED TARGETS — the call chain contains names for which no handler was found.

This is static source analysis of the loaded Story. It does not execute Osiris and cannot prove that a branch will be reached at runtime.
""", ["procedure analysis", "procedure", "call graph", "handler", "call sites", "INIT", "EXIT"])
    ];
}
