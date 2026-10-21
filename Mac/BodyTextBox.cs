using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
namespace Papergraph;

public sealed class BodyTextBox:TextBox
{
    protected override Type StyleKeyOverride=>typeof(TextBox);
    BodyTextPresenter? presenter;
    IReadOnlyList<BodyTextColor> colors=[];
    IReadOnlyList<BodyTextColor> backgrounds=[];
    (int Start,int Length,IBrush? Brush) highlight;
    public void SetColors(IReadOnlyList<BodyTextColor>? value){colors=value??[];presenter?.SetColors(colors);}
    public void SetBackgrounds(IReadOnlyList<BodyTextColor>? value){backgrounds=value??[];presenter?.SetBackgrounds(backgrounds);}
    public void SetHighlight(int start,int length,IBrush? brush){highlight=(start,length,brush);presenter?.SetHighlight(start,length,brush);}
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e){base.OnApplyTemplate(e);presenter=e.NameScope.Find<BodyTextPresenter>("PART_TextPresenter");presenter?.SetColors(colors);presenter?.SetBackgrounds(backgrounds);presenter?.SetHighlight(highlight.Start,highlight.Length,highlight.Brush);}
}
public sealed class BodyTextPresenter:TextPresenter
{
    IReadOnlyList<BodyTextColor> colors=[];
    IReadOnlyList<BodyTextColor> backgrounds=[];
    (int Start,int Length,IBrush? Brush) highlight;
    public void SetColors(IReadOnlyList<BodyTextColor> value){colors=value;InvalidateTextLayout();}
    public void SetBackgrounds(IReadOnlyList<BodyTextColor> value){backgrounds=value;InvalidateTextLayout();}
    public void SetHighlight(int start,int length,IBrush? brush){highlight=(start,length,brush);InvalidateTextLayout();}
    protected override TextLayout CreateTextLayout()
    {
        // Keep Avalonia's native IME composition, password and caret behavior intact.
        var original=base.CreateTextLayout();
        if((colors.Count==0&&backgrounds.Count==0&&highlight.Length==0)||!string.IsNullOrEmpty(PreeditText)||PasswordChar!=default)return original;
        var text=Text??"";var typeface=new Typeface(FontFamily,FontStyle,FontWeight,FontStretch);
        var start=Math.Min(SelectionStart,SelectionEnd);var end=Math.Max(SelectionStart,SelectionEnd);
        bool selectionVisible=ShowSelectionHighlight&&end>start;
        bool selected=selectionVisible&&SelectionForegroundBrush!=null;
        var cuts=new SortedSet<int>{0,text.Length};
        foreach(var c in colors){cuts.Add(Math.Clamp(c.Start,0,text.Length));cuts.Add(Math.Clamp(c.End,0,text.Length));}
        foreach(var c in backgrounds){cuts.Add(Math.Clamp(c.Start,0,text.Length));cuts.Add(Math.Clamp(c.End,0,text.Length));}
        cuts.Add(Math.Clamp(highlight.Start,0,text.Length));cuts.Add(Math.Clamp(highlight.Start+highlight.Length,0,text.Length));
        if(selectionVisible){cuts.Add(start);cuts.Add(end);}
        var positions=cuts.ToArray();var styles=new List<ValueSpan<TextRunProperties>>();
        for(int i=0;i<positions.Length-1;i++)
        {
            int a=positions[i],b=positions[i+1];if(b<=a)continue;
            IBrush? brush=selected&&a>=start&&a<end?SelectionForegroundBrush:null;
            if(brush==null&&colors.FirstOrDefault(c=>c.Start<=a&&c.End>a) is {} color)brush=new SolidColorBrush(Color.Parse(color.Color));
            var background=a>=highlight.Start&&a<highlight.Start+highlight.Length?highlight.Brush:null;
            if(backgrounds.FirstOrDefault(c=>c.Start<=a&&c.End>a) is {} fill)background=new SolidColorBrush(Color.Parse(fill.Color));
            // Let the native selection remain visible above persistent fills.
            if(selectionVisible&&a>=start&&a<end)background=null;
            if(brush!=null||background!=null)styles.Add(new(a,b-a,new GenericTextRunProperties(typeface,FontFeatures,FontSize,foregroundBrush:brush??Foreground,backgroundBrush:background)));
        }
        var result=new TextLayout(text,typeface,FontFeatures,FontSize,Foreground,TextAlignment,TextWrapping,maxWidth:original.MaxWidth,maxHeight:original.MaxHeight,textStyleOverrides:styles,flowDirection:FlowDirection,lineHeight:LineHeight,letterSpacing:LetterSpacing);
        original.Dispose();return result;
    }
}
