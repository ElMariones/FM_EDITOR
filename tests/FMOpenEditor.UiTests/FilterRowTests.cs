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
}
