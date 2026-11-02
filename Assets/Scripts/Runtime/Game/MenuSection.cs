// MenuSection.cs
// Remake: a section of an in-flight menu's body (the Mining Plant's, PauseMenu.Choose): a heading, a line under it, then
// rows like the cargo hold's (an item's icon and name, its tons, a note on what it becomes or what is missing), or a line
// when there is nothing to list. Made by the rules (MiningPlant.Section, WasteProcessing.Section), drawn by the HUD.

using System.Collections.Generic;

namespace GoF2Remake.Data
{
    public class MenuSection
    {
        public string heading, text;
        /// <summary>Shown instead of the rows when there are none.</summary>
        public string empty;
        public List<MenuRow> rows = new List<MenuRow>();
    }

    /// <summary>A row: the item, its amount as shown ("62 t"), the note beside it; 'ready' notes are highlighted (the menu's
    /// action does something with this row), the others dimmed (what is missing).</summary>
    public class MenuRow
    {
        public int item;
        public string amount, note;
        public bool ready;
    }
}
