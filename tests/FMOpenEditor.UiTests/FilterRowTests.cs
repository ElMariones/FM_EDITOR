using FMOpenEditor.App.ViewModels;
using FMOpenEditor.Core.Data;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;
using FMOpenEditor.Core.Search;

namespace FMOpenEditor.UiTests;

public class FilterRowTests
{
    [Fact]
    public void Position_row_finds_players_rated_2_or_more()
    {
        using var s = new EditorSession(new DemoDatabase());
        var props = PropertyCatalog.For(EntityKind.Player);
        var row = new FilterRowViewModel(s, props) { SelectedProperty = props.First(p => p.Key == "Pos.ST") };

        Assert.False(row.HasOp);
        Assert.False(row.NeedsValue);
        Assert.Equal(new Filter("Pos.ST", FilterOp.AtLeast, 2), row.ToFilter());

        row.SelectedProperty = props.First(p => p.Key == "Age");
        Assert.True(row.HasOp);
        Assert.True(row.NeedsValue);
    }

    [Fact]
    public void Category_box_limits_the_property_box()
    {
        using var s = new EditorSession(new DemoDatabase());
        var row = new FilterRowViewModel(s, PropertyCatalog.For(EntityKind.Player));
        Assert.Equal("Age", row.SelectedProperty!.Key);
        Assert.Equal(row.SelectedProperty.Category, row.SelectedCategory);

        row.SelectedCategory = "Technical";
        Assert.All(row.CategoryProperties, p => Assert.Equal("Technical", p.Category));
        Assert.Equal(row.CategoryProperties[0], row.SelectedProperty);
    }

    [Fact]
    public void Yes_no_rows_pick_yes_or_no()
    {
        using var s = new EditorSession(new DemoDatabase());
        var props = PropertyCatalog.For(EntityKind.Player);
        var row = new FilterRowViewModel(s, props) { SelectedProperty = props.First(p => p.Key == "IsInjured") };

        Assert.False(row.HasOp);
        Assert.False(row.NeedsValue);
        Assert.Equal(["Yes", "No"], row.ValueChoices);
        Assert.Equal(new Filter("IsInjured", FilterOp.Equals, true), row.ToFilter());
        row.SelectedValueChoice = "No";
        Assert.Equal(new Filter("IsInjured", FilterOp.Equals, false), row.ToFilter());
    }

    [Fact]
    public void List_rows_pick_from_the_games_list()
    {
        using var s = new EditorSession(new DemoDatabase());
        var props = PropertyCatalog.For(EntityKind.Player);
        var status = props.First(p => p.Key == "Contract.SquadStatus");
        var row = new FilterRowViewModel(s, props) { SelectedProperty = status };

        Assert.True(row.IsChoice);
        Assert.False(row.NeedsValue);
        Assert.Equal(status.Choices, row.ValueChoices);
        row.SelectedValueChoice = "Star Player";
        row.ChoiceOp = OpOption.ForChoice[1];
        var f = row.ToFilter()!;
        Assert.Equal(FilterOp.NotEquals, f.Op);
        Assert.Equal(status.Choices!.ToList().IndexOf("Star Player"), f.Value);

        // The search engine agrees: "is not Star Player" never returns a Star Player.
        var hits = SearchEngine.Run(s, new SearchQuery { Kind = EntityKind.Player, Filters = [f] });
        Assert.All(hits, e => Assert.NotEqual(f.Value, s.TryRead(e, "Contract.SquadStatus")));
    }
}
