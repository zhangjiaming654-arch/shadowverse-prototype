using Shadowverse.Engine.Cards;
using Shadowverse.Engine.Decks;
using Shadowverse.Engine.Models;

namespace Shadowverse.DeckEditor;

public sealed class DeckEditorForm : Form
{
    private readonly ComboBox _savedDecks = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly TextBox _deckName = new() { Width = 250 };
    private readonly Label _cardCount = new() { AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
    private readonly Label _deckSummary = new() { AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
    private readonly ManaCurveChart _costCurve = new() { Dock = DockStyle.Fill };
    private readonly TextBox _catalogSearch = new() { Width = 190, PlaceholderText = "按卡牌名称搜索" };
    private readonly ComboBox _professionFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
    private readonly ComboBox _rarityFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ComboBox _typeFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly ComboBox _costFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
    private readonly ComboBox _keywordFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly DataGridView _deckGrid = CreateDeckGrid();
    private readonly DataGridView _catalogGrid = CreateCatalogGrid();
    private readonly FlowLayoutPanel _cardGallery = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = SvTheme.Panel, Padding = new Padding(3) };
    private readonly Label _catalogCount = new() { AutoSize = true, ForeColor = SvTheme.TextDim, Padding = new Padding(8, 8, 0, 0) };
    private readonly TextBox _cardDetail = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, TabStop = false,
        Text = "将鼠标停在卡牌上查看完整能力。单击卡牌加入卡组。" };
    private readonly List<DeckCardEntry> _entries = [];
    private string? _editingDeckId;
    private bool _isLoading;

    public DeckEditorForm()
    {
        Text = "影之诗原型 - 卡组编辑器";
        BackColor = SvTheme.Void;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1120, 760);
        Size = new Size(1340, 900);
        Font = new Font("Microsoft YaHei UI", 9f);
        DoubleBuffered = true;

        BuildLayout();
        RefreshCatalogGrid();
        LoadSavedDecks(null);

        // 《影之诗：超凡世界》风格：背景贴图 + 深色控件
        SvTheme.AttachBackdrop(this);
        SvTheme.ApplyTo(this);
        RefreshDeckStatistics();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 12, 18, 12),
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new SvHeading("卡组编成", "选择卡牌 · 调整构筑 · 准备对战"), 0, 0);
        root.Controls.Add(CreateTopSection(), 0, 1);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        body.Controls.Add(CreateDeckSection(), 0, 0);
        body.Controls.Add(CreateCatalogSection(), 1, 0);
        root.Controls.Add(body, 0, 2);
        Controls.Add(root);

        _savedDecks.SelectedIndexChanged += (_, _) => LoadSelectedDeck();
        _catalogSearch.TextChanged += (_, _) => RefreshCatalogGrid();
        _professionFilter.SelectedIndexChanged += (_, _) => RefreshCatalogGrid();
        _rarityFilter.SelectedIndexChanged += (_, _) => RefreshCatalogGrid();
        _typeFilter.SelectedIndexChanged += (_, _) => RefreshCatalogGrid();
        _costFilter.SelectedIndexChanged += (_, _) => RefreshCatalogGrid();
        _keywordFilter.SelectedIndexChanged += (_, _) => RefreshCatalogGrid();
        _catalogGrid.CellClick += CatalogGridCellClick;
        _deckGrid.CellContentClick += DeckGridCellContentClick;
        _deckGrid.CellValidating += DeckGridCellValidating;
        _deckGrid.CellEndEdit += DeckGridCellEndEdit;
    }

    private Control CreateTopSection()
    {
        var controls = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true,
            Padding = new Padding(0, 10, 0, 10), Margin = new Padding(0), BackColor = Color.Transparent };
        var newDeckButton = new SvButton { Text = "新建卡组", Size = new Size(100, 34) };
        var saveButton = new SvButton { Text = "保存卡组", Size = new Size(100, 34), Primary = true };
        var replayButton = new SvButton { Text = "对局回放  →", Size = new Size(128, 34) };
        newDeckButton.Click += (_, _) => StartNewDeck();
        saveButton.Click += (_, _) => SaveDeck();
        replayButton.Click += (_, _) => { using var replay = new ReplayForm(); replay.ShowDialog(this); };
        _savedDecks.Width = 230;
        _deckName.Width = 220;
        _savedDecks.Margin = _deckName.Margin = new Padding(3, 7, 14, 3);
        controls.Controls.AddRange([
            new Label { Text = "已保存卡组", AutoSize = true, Padding = new Padding(0, 10, 0, 0) }, _savedDecks,
            new Label { Text = "卡组名称", AutoSize = true, Padding = new Padding(0, 10, 0, 0) }, _deckName,
            newDeckButton, saveButton, replayButton]);
        return controls;
    }

    private Control CreateDeckSection()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _cardCount.Dock = DockStyle.Fill;
        _cardCount.Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold);
        _cardCount.Padding = new Padding(0, 4, 0, 0);
        layout.Controls.Add(_cardCount, 0, 0);
        layout.Controls.Add(_deckSummary, 0, 1);
        layout.Controls.Add(_costCurve, 0, 2);
        layout.Controls.Add(_deckGrid, 0, 3);
        foreach (var name in new[] { "Id", "Profession", "Type", "Effect" }) _deckGrid.Columns[name]!.Visible = false;
        _deckGrid.Columns["Name"]!.FillWeight = 35;
        _deckGrid.Columns["Name"]!.MinimumWidth = 105;
        _deckGrid.Columns["Cost"]!.FillWeight = 10;
        _deckGrid.Columns["Rarity"]!.FillWeight = 12;
        _deckGrid.Columns["Body"]!.FillWeight = 12;
        _deckGrid.Columns["Count"]!.FillWeight = 10;
        _deckGrid.Columns["Decrease"]!.FillWeight = 9;
        _deckGrid.Columns["Increase"]!.FillWeight = 9;
        _deckGrid.Columns["Decrease"]!.HeaderText = "－";
        _deckGrid.Columns["Increase"]!.HeaderText = "＋";
        foreach (var name in new[] { "Cost", "Rarity", "Body", "Count" }) _deckGrid.Columns[name]!.MinimumWidth = 44;
        _deckGrid.Columns["Decrease"]!.MinimumWidth = 30;
        _deckGrid.Columns["Increase"]!.MinimumWidth = 30;
        _deckGrid.CellMouseEnter += (_, e) => {
            if (e.RowIndex >= 0 && _deckGrid.Rows[e.RowIndex].Cells["Id"].Value is string id) ShowCardDetail(CardCatalog.Get(id));
        };
        return SvTheme.Frame(layout, "当前卡组    /    编辑张数或使用 ＋／－");
    }

    private Control CreateCatalogSection()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));

        var title = new Label
        {
            Text = "单击卡牌加入 1 张 · 悬停查看完整能力",
            ForeColor = SvTheme.TextDim,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 4)
        };
        var filters = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 5)
        };
        var clearButton = new SvButton { Text = "重置", Size = new Size(70, 30) };
        var viewButton = new SvButton { Text = "列表视图", Size = new Size(96, 30) };
        viewButton.Click += (_, _) => {
            _catalogGrid.Visible = !_catalogGrid.Visible;
            _cardGallery.Visible = !_catalogGrid.Visible;
            viewButton.Text = _catalogGrid.Visible ? "卡牌视图" : "列表视图";
        };
        clearButton.Click += (_, _) => ClearCatalogFilters();

        _professionFilter.Items.AddRange(
        [
            "全部职业", "精灵", "皇家护卫", "巫师", "龙族",
            "梦魇", "主教", "超越者", "中立"
        ]);
        _rarityFilter.Items.AddRange(["全部稀有度", "铜卡", "银卡", "金卡", "虹卡"]);
        _typeFilter.Items.AddRange(["全部类型", "随从", "法术", "护符"]);
        _costFilter.Items.Add("全部费用");
        foreach (var cost in Enumerable.Range(1, 7))
        {
            _costFilter.Items.Add($"{cost}费");
        }
        _costFilter.Items.Add("8+费");

        _keywordFilter.Items.AddRange(["全部关键词", "守护", "疾驰", "突进", "毁灭", "威慑", "无视守护", "屏障", "灵气", "虹吸", "无关键词"]);
        _professionFilter.SelectedIndex = 0;
        _rarityFilter.SelectedIndex = 0;
        _typeFilter.SelectedIndex = 0;
        _costFilter.SelectedIndex = 0;
        _keywordFilter.SelectedIndex = 0;

        Control FilterField(string label, Control input)
        {
            var field = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 8, 0) };
            field.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
            field.Controls.Add(input);
            return field;
        }
        filters.Controls.AddRange([
            FilterField("搜索：", _catalogSearch), FilterField("职业：", _professionFilter),
            FilterField("稀有度：", _rarityFilter), FilterField("类型：", _typeFilter),
            FilterField("费用：", _costFilter), FilterField("关键词：", _keywordFilter),
            clearButton, viewButton, _catalogCount]);

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(filters, 0, 1);
        var collection = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 8) };
        _catalogGrid.Visible = false;
        collection.Controls.Add(_catalogGrid);
        collection.Controls.Add(_cardGallery);
        panel.Controls.Add(collection, 0, 2);
        panel.Controls.Add(SvTheme.Frame(_cardDetail, "卡牌详情"), 0, 3);
        return SvTheme.Frame(panel, "卡牌库");
    }

    private static Control CreateSection(string title, DataGridView grid)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 5, 0, 5)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = title, AutoSize = true, Padding = new Padding(0, 0, 0, 4) }, 0, 0);
        panel.Controls.Add(grid, 0, 1);
        return panel;
    }

    private static DataGridView CreateDeckGrid()
    {
        var grid = CreateGrid(readOnly: false);
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Count",
            HeaderText = "张数",
            FillWeight = 8,
            ValueType = typeof(int)
        });
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Decrease",
            HeaderText = "减少",
            Text = "－",
            UseColumnTextForButtonValue = true,
            FillWeight = 6
        });
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Increase",
            HeaderText = "增加",
            Text = "＋",
            UseColumnTextForButtonValue = true,
            FillWeight = 6
        });
        return grid;
    }

    private static DataGridView CreateCatalogGrid() => CreateGrid(readOnly: true);

    private static DataGridView CreateGrid(bool readOnly)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = readOnly,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "编号", FillWeight = 10, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "卡牌名称", FillWeight = 15, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Profession", HeaderText = "职业", FillWeight = 9, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rarity", HeaderText = "稀有度", FillWeight = 8, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "类型", FillWeight = 8, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cost", HeaderText = "费用", FillWeight = 6, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Body", HeaderText = "身材", FillWeight = 8, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Effect", HeaderText = "效果", FillWeight = 25, ReadOnly = true });

        return grid;
    }

    private void CatalogGridCellClick(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (eventArgs.RowIndex >= 0)
        {
            AddCard(_catalogGrid.Rows[eventArgs.RowIndex].Cells["Id"].Value?.ToString());
        }
    }

    private void DeckGridCellContentClick(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0)
        {
            return;
        }

        var cardId = _deckGrid.Rows[eventArgs.RowIndex].Cells["Id"].Value?.ToString();
        var columnName = _deckGrid.Columns[eventArgs.ColumnIndex].Name;
        if (columnName == "Increase")
        {
            AddCard(cardId);
        }
        else if (columnName == "Decrease")
        {
            ChangeCardCount(cardId, -1);
        }
    }

    private void DeckGridCellValidating(object? sender, DataGridViewCellValidatingEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || _deckGrid.Columns[eventArgs.ColumnIndex].Name != "Count")
        {
            return;
        }

        var cardId = _deckGrid.Rows[eventArgs.RowIndex].Cells["Id"].Value?.ToString();
        var entry = _entries.FirstOrDefault(item => item.CardId == cardId);
        if (entry is null)
        {
            eventArgs.Cancel = true;
            return;
        }

        if (!int.TryParse(eventArgs.FormattedValue?.ToString(), out var requestedCount) || requestedCount < 0)
        {
            eventArgs.Cancel = true;
            _deckGrid.Rows[eventArgs.RowIndex].ErrorText = "张数必须是 0 或正整数。";
            return;
        }

        var maximumForThisCard = DeckDefinition.RequiredCardCount - TotalCardCount() + entry.Count;
        if (requestedCount > maximumForThisCard)
        {
            eventArgs.Cancel = true;
            _deckGrid.Rows[eventArgs.RowIndex].ErrorText = $"该卡最多可设为 {maximumForThisCard} 张，卡组不能超过 {DeckDefinition.RequiredCardCount} 张。";
            return;
        }

        _deckGrid.Rows[eventArgs.RowIndex].ErrorText = string.Empty;
    }

    private void DeckGridCellEndEdit(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || _deckGrid.Columns[eventArgs.ColumnIndex].Name != "Count")
        {
            return;
        }

        var cardId = _deckGrid.Rows[eventArgs.RowIndex].Cells["Id"].Value?.ToString();
        if (int.TryParse(_deckGrid.Rows[eventArgs.RowIndex].Cells["Count"].Value?.ToString(), out var requestedCount))
        {
            SetCardCount(cardId, requestedCount);
        }
    }

    private void RefreshCatalogGrid()
    {
        if (_professionFilter.SelectedIndex < 0 || _rarityFilter.SelectedIndex < 0 || _typeFilter.SelectedIndex < 0 ||
            _costFilter.SelectedIndex < 0 || _keywordFilter.SelectedIndex < 0)
        {
            return;
        }

        var searchText = _catalogSearch.Text.Trim();
        var professionFilter = _professionFilter.SelectedItem?.ToString();
        var rarityFilter = _rarityFilter.SelectedItem?.ToString();
        var typeFilter = _typeFilter.SelectedItem?.ToString();
        var costFilter = _costFilter.SelectedItem?.ToString();
        var keywordFilter = _keywordFilter.SelectedItem?.ToString();

        var cards = CardCatalog.All.Where(card =>
            card.IsCollectible &&
            (string.IsNullOrWhiteSpace(searchText) || MatchesSearch(card, searchText)) &&
            MatchesProfession(card, professionFilter) &&
            MatchesRarity(card, rarityFilter) &&
            MatchesType(card, typeFilter) &&
            MatchesCost(card, costFilter) &&
            MatchesKeyword(card, keywordFilter));

        var filtered = cards.ToArray();
        _catalogCount.Text = $"{filtered.Length} 张卡牌";
        _cardGallery.SuspendLayout();
        var previous = _cardGallery.Controls.Cast<Control>().ToArray();
        _cardGallery.Controls.Clear();
        foreach (var old in previous) old.Dispose();
        _catalogGrid.Rows.Clear();
        foreach (var card in filtered)
        {
            _catalogGrid.Rows.Add(
                card.Id,
                card.Name,
                CardProfessionName(card.Profession),
                CardRarityName(card.Rarity),
                CardTypeName(card.Type),
                $"{card.Cost}费",
                CardBody(card),
                EffectDescription(card));
            var accent = card.Rarity switch { CardRarity.Rainbow => Color.FromArgb(174, 151, 220),
                CardRarity.Gold => SvTheme.GoldLit, CardRarity.Silver => Color.FromArgb(183, 204, 220), _ => Color.FromArgb(158, 126, 99) };
            var tile = new SvLibraryCard(card.Name, card.Cost, CardProfessionName(card.Profession), CardRarityName(card.Rarity),
                $"{CardTypeName(card.Type)}  {CardBody(card)}", EffectDescription(card), accent);
            tile.Size = new Size((int)(152 * DeviceDpi / 96f), (int)(190 * DeviceDpi / 96f));
            tile.Click += (_, _) => AddCard(card.Id);
            tile.MouseEnter += (_, _) => ShowCardDetail(card);
            tile.GotFocus += (_, _) => ShowCardDetail(card);
            _cardGallery.Controls.Add(tile);
        }
        if (filtered.Length == 0) _cardGallery.Controls.Add(new Label { Text = "没有匹配的卡牌，请调整筛选条件。", AutoSize = true, ForeColor = SvTheme.TextDim, Padding = new Padding(12) });
        _cardGallery.ResumeLayout();
    }

    private void ShowCardDetail(CardDefinition card)
    {
        _cardDetail.Text = $"{card.Name}  ·  {card.Cost}费  ·  {CardProfessionName(card.Profession)}  ·  {CardRarityName(card.Rarity)}\r\n{EffectDescription(card)}";
    }

    private void ClearCatalogFilters()
    {
        _catalogSearch.Clear();
        _professionFilter.SelectedIndex = 0;
        _rarityFilter.SelectedIndex = 0;
        _typeFilter.SelectedIndex = 0;
        _costFilter.SelectedIndex = 0;
        _keywordFilter.SelectedIndex = 0;
    }

    private static bool MatchesSearch(CardDefinition card, string searchText)
    {
        return card.Id.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
               card.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
               EffectDescription(card).Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesType(CardDefinition card, string? typeFilter) => typeFilter switch
    {
        "随从" => card.Type == CardType.Follower,
        "法术" => card.Type == CardType.Spell,
        "护符" => card.Type == CardType.Amulet,
        _ => true
    };

    private static bool MatchesRarity(CardDefinition card, string? rarityFilter) => rarityFilter switch
    {
        "铜卡" => card.Rarity == CardRarity.Bronze,
        "银卡" => card.Rarity == CardRarity.Silver,
        "金卡" => card.Rarity == CardRarity.Gold,
        "虹卡" => card.Rarity == CardRarity.Rainbow,
        _ => true
    };

    private static bool MatchesProfession(CardDefinition card, string? professionFilter) => professionFilter switch
    {
        "精灵" => card.Profession == CardProfession.Elf,
        "皇家护卫" => card.Profession == CardProfession.Royal,
        "巫师" => card.Profession == CardProfession.Witch,
        "龙族" => card.Profession == CardProfession.Dragon,
        "梦魇" => card.Profession == CardProfession.Nightmare,
        "主教" => card.Profession == CardProfession.Bishop,
        "超越者" => card.Profession == CardProfession.Nemesis,
        "中立" => card.Profession == CardProfession.Neutral,
        _ => true
    };

    private static bool MatchesCost(CardDefinition card, string? costFilter)
    {
        if (costFilter is null or "全部费用")
        {
            return true;
        }

        if (costFilter == "8+费")
        {
            return card.Cost >= 8;
        }

        return int.TryParse(costFilter[..^1], out var cost) && card.Cost == cost;
    }

    private static bool MatchesKeyword(CardDefinition card, string? keywordFilter) => keywordFilter switch
    {
        "守护" => card.Keywords.HasFlag(CardKeyword.Ward),
        "疾驰" => card.Keywords.HasFlag(CardKeyword.Storm),
        "突进" => card.Keywords.HasFlag(CardKeyword.Rush),
        "毁灭" => card.Keywords.HasFlag(CardKeyword.Bane),
        "威慑" => card.Keywords.HasFlag(CardKeyword.Intimidate),
        "无视守护" => card.Keywords.HasFlag(CardKeyword.IgnoreWard),
        "屏障" => card.Keywords.HasFlag(CardKeyword.Barrier),
        "灵气" => card.Keywords.HasFlag(CardKeyword.Aura),
        "虹吸" => card.Keywords.HasFlag(CardKeyword.Drain),
        "无关键词" => card.Keywords == CardKeyword.None,
        _ => true
    };

    private void LoadSavedDecks(string? selectDeckId)
    {
        _isLoading = true;
        _savedDecks.Items.Clear();
        foreach (var deck in DeckCatalog.All)
        {
            _savedDecks.Items.Add(new DeckChoice(deck.Id, deck.Name));
        }

        if (_savedDecks.Items.Count == 0)
        {
            _savedDecks.SelectedIndex = -1;
            _isLoading = false;
            StartNewDeck();
            return;
        }

        var selectedIndex = _savedDecks.Items
            .Cast<DeckChoice>()
            .ToList()
            .FindIndex(choice => choice.Id == selectDeckId);
        _savedDecks.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        _isLoading = false;
        LoadSelectedDeck();
    }

    private void LoadSelectedDeck()
    {
        if (_isLoading || _savedDecks.SelectedItem is not DeckChoice selected)
        {
            return;
        }

        var deck = DeckCatalog.Get(selected.Id);
        _editingDeckId = deck.Id;
        _deckName.Text = deck.Name;
        _entries.Clear();
        _entries.AddRange(deck.Entries.Select(entry => new DeckCardEntry(entry.CardId, entry.Count)));
        RefreshDeckGrid();
    }

    private void StartNewDeck()
    {
        _editingDeckId = null;
        _isLoading = true;
        _savedDecks.SelectedIndex = -1;
        _isLoading = false;
        _deckName.Text = "新卡组";
        _entries.Clear();
        RefreshDeckGrid();
    }

    private void AddCard(string? cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        var card = CardCatalog.Get(cardId);
        if (!card.IsCollectible)
        {
            MessageBox.Show(
                "启示录牌组中的衍生卡不能直接加入普通卡组。",
                "无法加入",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (TotalCardCount() >= DeckDefinition.RequiredCardCount)
        {
            MessageBox.Show(
                $"卡组已经是 {DeckDefinition.RequiredCardCount} 张，不能继续加入。",
                "卡组已满",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var existingIndex = _entries.FindIndex(entry => entry.CardId == cardId);
        if (existingIndex >= 0)
        {
            var existing = _entries[existingIndex];
            _entries[existingIndex] = existing with { Count = existing.Count + 1 };
        }
        else
        {
            _entries.Add(new DeckCardEntry(cardId, 1));
        }

        RefreshDeckGrid();
    }

    private void ChangeCardCount(string? cardId, int change)
    {
        var entry = _entries.FirstOrDefault(item => item.CardId == cardId);
        if (entry is null)
        {
            return;
        }

        SetCardCount(cardId, entry.Count + change);
    }

    private void SetCardCount(string? cardId, int requestedCount)
    {
        var index = _entries.FindIndex(entry => entry.CardId == cardId);
        if (index < 0 || requestedCount < 0)
        {
            return;
        }

        var previous = _entries[index];
        var newTotal = TotalCardCount() - previous.Count + requestedCount;
        if (newTotal > DeckDefinition.RequiredCardCount)
        {
            MessageBox.Show(
                $"卡组不能超过 {DeckDefinition.RequiredCardCount} 张。",
                "卡组已满",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (requestedCount == 0)
        {
            _entries.RemoveAt(index);
        }
        else
        {
            _entries[index] = previous with { Count = requestedCount };
        }

        RefreshDeckGrid();
    }

    private void RefreshDeckGrid()
    {
        _deckGrid.Rows.Clear();
        foreach (var entry in _entries.OrderBy(entry => CardCatalog.Get(entry.CardId).Cost).ThenBy(entry => entry.CardId))
        {
            var card = CardCatalog.Get(entry.CardId);
            _deckGrid.Rows.Add(
                card.Id,
                card.Name,
                CardProfessionName(card.Profession),
                CardRarityName(card.Rarity),
                CardTypeName(card.Type),
                $"{card.Cost}费",
                CardBody(card),
                EffectDescription(card),
                entry.Count);
        }

        RefreshDeckStatistics();
    }

    private void RefreshDeckStatistics()
    {
        var currentCount = TotalCardCount();
        _cardCount.Text = currentCount == DeckDefinition.RequiredCardCount
            ? $"当前张数：{currentCount}/{DeckDefinition.RequiredCardCount}（可保存）"
            : $"当前张数：{currentCount}/{DeckDefinition.RequiredCardCount}（还差 {DeckDefinition.RequiredCardCount - currentCount} 张）";
        _cardCount.ForeColor = currentCount == DeckDefinition.RequiredCardCount ? SvTheme.Cyan : SvTheme.GoldLit;

        var followers = _entries.Where(entry => CardCatalog.Get(entry.CardId).Type == CardType.Follower).Sum(entry => entry.Count);
        var spells = _entries.Where(entry => CardCatalog.Get(entry.CardId).Type == CardType.Spell).Sum(entry => entry.Count);
        var amulets = _entries.Where(entry => CardCatalog.Get(entry.CardId).Type == CardType.Amulet).Sum(entry => entry.Count);
        _deckSummary.Text = $"构成：随从 {followers}　法术 {spells}　护符 {amulets}";

        var costs = _entries
            .GroupBy(entry => CardCatalog.Get(entry.CardId).Cost)
            .Select(group => new KeyValuePair<int, int>(group.Key, group.Sum(entry => entry.Count)));
        _costCurve.SetCounts(costs);
    }

    private void SaveDeck()
    {
        var deckName = _deckName.Text.Trim();
        if (string.IsNullOrWhiteSpace(deckName))
        {
            MessageBox.Show("请先填写卡组名称。", "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // A deck may be saved while it is still being assembled; only the deck limit is enforced.
        if (TotalCardCount() is 0 || TotalCardCount() > DeckDefinition.RequiredCardCount)
        {
            MessageBox.Show(
                $"卡组不能为空、也不能超过 {DeckDefinition.RequiredCardCount} 张，当前为 {TotalCardCount()} 张。\n未满 {DeckDefinition.RequiredCardCount} 张也可以保存，但不满 {DeckDefinition.RequiredCardCount} 张的卡组暂时不能用于对局。",
                "无法保存",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var savedDecks = DeckCatalog.All.ToList();
        var deckId = _editingDeckId ?? NextDeckId(savedDecks);
        var savedDeck = new DeckListDefinition(
            deckId,
            deckName,
            "由卡组编辑器创建。",
            _entries.Select(entry => new DeckCardEntry(entry.CardId, entry.Count)).ToArray());
        var existingIndex = savedDecks.FindIndex(deck => deck.Id == deckId);
        if (existingIndex >= 0)
        {
            savedDecks[existingIndex] = savedDeck;
        }
        else
        {
            savedDecks.Add(savedDeck);
        }

        try
        {
            DeckCatalog.Save(savedDecks);
            _editingDeckId = deckId;
            LoadSavedDecks(deckId);
            MessageBox.Show($"已保存 {deckId}：{deckName}", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private int TotalCardCount() => _entries.Sum(entry => entry.Count);

    private static string NextDeckId(IEnumerable<DeckListDefinition> decks)
    {
        var highestNumber = decks
            .Select(deck => deck.Id.StartsWith("DECK-", StringComparison.Ordinal) &&
                            int.TryParse(deck.Id[5..], out var number)
                ? number
                : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"DECK-{highestNumber + 1:D3}";
    }

    private static string CardTypeName(CardType type) => type switch
    {
        CardType.Follower => "随从",
        CardType.Spell => "法术",
        CardType.Amulet => "护符",
        _ => throw new InvalidOperationException("Unknown card type.")
    };

    private static string CardRarityName(CardRarity rarity) => rarity switch
    {
        CardRarity.Bronze => "铜卡",
        CardRarity.Silver => "银卡",
        CardRarity.Gold => "金卡",
        CardRarity.Rainbow => "虹卡",
        _ => throw new InvalidOperationException("Unknown card rarity.")
    };

    private static string CardProfessionName(CardProfession profession) => profession switch
    {
        CardProfession.Elf => "精灵",
        CardProfession.Royal => "皇家护卫",
        CardProfession.Witch => "巫师",
        CardProfession.Dragon => "龙族",
        CardProfession.Nightmare => "梦魇",
        CardProfession.Bishop => "主教",
        CardProfession.Nemesis => "超越者",
        CardProfession.Neutral => "中立",
        _ => throw new InvalidOperationException("Unknown card profession.")
    };

    private static string CardBody(CardDefinition card) =>
        card.Type == CardType.Follower ? $"{card.Attack}/{card.Defense}" : string.Empty;

    private static string EffectDescription(CardDefinition card)
    {
        var effects = new List<string>();
        if (card.Keywords.HasFlag(CardKeyword.Ward) && !card.EffectText.Contains("【守护】", StringComparison.Ordinal))
        {
            effects.Add("守护");
        }

        if (card.Keywords.HasFlag(CardKeyword.Storm) && !card.EffectText.Contains("【疾驰】", StringComparison.Ordinal))
        {
            effects.Add("疾驰");
        }

        if (card.Keywords.HasFlag(CardKeyword.Bane) && !card.EffectText.Contains("【毁灭】", StringComparison.Ordinal))
        {
            effects.Add("毁灭");
        }

        if (card.Keywords.HasFlag(CardKeyword.Intimidate) && !card.EffectText.Contains("【威慑】", StringComparison.Ordinal))
        {
            effects.Add("威慑");
        }

        if (card.Keywords.HasFlag(CardKeyword.Rush) && !card.EffectText.Contains("【突进】", StringComparison.Ordinal))
        {
            effects.Add("突进");
        }

        if (card.Keywords.HasFlag(CardKeyword.IgnoreWard) && !card.EffectText.Contains("无视【守护】", StringComparison.Ordinal))
        {
            effects.Add("无视守护攻击");
        }

        if (card.Keywords.HasFlag(CardKeyword.Barrier) && !card.EffectText.Contains("【屏障】", StringComparison.Ordinal))
        {
            effects.Add("屏障");
        }

        if (card.Keywords.HasFlag(CardKeyword.Aura) && !card.EffectText.Contains("【灵气】", StringComparison.Ordinal))
        {
            effects.Add("灵气");
        }

        if (card.Keywords.HasFlag(CardKeyword.Drain) && !card.EffectText.Contains("【虹吸】", StringComparison.Ordinal))
        {
            effects.Add("虹吸");
        }

        if (!string.IsNullOrWhiteSpace(card.EffectText))
        {
            effects.Add(card.EffectText);
        }

        return effects.Count == 0 ? "无" : string.Join("；", effects);
    }

    private sealed record DeckChoice(string Id, string Name)
    {
        public override string ToString() => $"{Id} - {Name}";
    }
}
