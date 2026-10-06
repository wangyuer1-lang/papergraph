using System.Globalization;
using System.Text;

namespace Papergraph;

// Counts the manuscript text, independently of graph objects and visual line wrapping.
internal readonly record struct TextStatistics(int Words,int Characters)
{
    internal static TextStatistics Count(string text)
    {
        int characters=0,words=0;
        var elements=StringInfo.GetTextElementEnumerator(text);
        while(elements.MoveNext())
        {
            var first=Rune.GetRuneAt(elements.GetTextElement(),0);
            if(!Rune.IsWhiteSpace(first)&&Rune.GetUnicodeCategory(first) is not (UnicodeCategory.Control or UnicodeCategory.Format))characters++;
        }
        var runes=text.EnumerateRunes().ToArray();bool inWord=false;
        for(int i=0;i<runes.Length;i++)
        {
            var rune=runes[i];
            if(IsEastAsian(rune.Value)){words++;inWord=false;}
            else if(Rune.IsLetterOrDigit(rune)){if(!inWord)words++;inWord=true;}
            else if(Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)continue;
            else if(inWord&&i+1<runes.Length&&Rune.IsLetterOrDigit(runes[i+1])&&!IsEastAsian(runes[i+1].Value)&&
                (rune.Value is '\'' or '\u2019' or '-' or '\u2010' or '\u2011'||
                 rune.Value=='.'&&i>0&&Rune.IsDigit(runes[i-1])&&Rune.IsDigit(runes[i+1])))continue;
            else inWord=false;
        }
        return new(words,characters);
    }
    static bool IsEastAsian(int value)=>value is
        0x3005 or 0x3007 or
        >=0x3400 and <=0x4DBF or >=0x4E00 and <=0x9FFF or >=0xF900 and <=0xFAFF or
        >=0x20000 and <=0x323AF or
        >=0x3041 and <=0x3096 or >=0x309D and <=0x309F or
        >=0x30A1 and <=0x30FA or >=0x30FC and <=0x30FF or >=0x31F0 and <=0x31FF or
        >=0xFF66 and <=0xFF9D;
}
