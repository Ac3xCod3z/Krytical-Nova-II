using PSD.Creator.Editor.Inspector.Editors;
using PSD.Creator.Inspector;
using CTFX2.Creator.Data;
using UnityEditor;
using CTFX2.Creator.Tool;

namespace CTFX2.Creator.Editor.Inspector.Editors
{
    [CustomEditor(typeof(CartoonFX))]
    public class CartoonFXEditor : PSDCreatorEditor<CartoonFX, CartoonFXBasic, CartoonFXRandomizationPage>
    {
        protected override BasicSettingsPage BasicSettingsPage => new CartoonFXBasicSettingsPage();
        protected override RandomizationPage RandomizationPage => new CartoonFXRandomizationPage();
        protected override string Title => "Cartoon FX Creator";
        protected override string Reference => "Cartoon FX";

        protected override void AddPages(PageManager manager)
        {
            manager.AddPage(new TextPage());
            manager.AddPage(new BackgroundPage());
            manager.AddPage(new HalftonePage());
            manager.AddPage(new EffectsPage());
            manager.AddPage(new ImpactPage());
            manager.AddPage(new DebrisPage());
            manager.AddPage(new SmokePage());
        }
    }
}