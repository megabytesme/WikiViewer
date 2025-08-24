using System;
using System.Collections.Generic;
using System.Linq;
using WikiViewer.Shared.Uwp.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace WikiViewer.Shared.Uwp.Controls
{
    public sealed partial class SymbolPickerDialog : ContentDialog
    {
        public string SelectedEntity { get; private set; }
        private readonly List<SymbolViewModel> _allSymbols;
        private SymbolViewModel _selectedSymbol;

        public SymbolPickerDialog()
        {
            this.InitializeComponent();
            this.Loaded += SymbolPickerDialog_Loaded;
            _allSymbols = GetSymbolList();
            SymbolGridView.ItemsSource = _allSymbols;
            this.IsPrimaryButtonEnabled = false;
        }

        private void SymbolPickerDialog_Loaded(object sender, RoutedEventArgs e)
        {
#if UWP_1809
            var scrollViewer =
                (this.ContentTemplateRoot as FrameworkElement)?.FindName("SymbolGridView")
                as GridView;
            if (scrollViewer != null)
            {
                var parentScrollViewer = VisualTreeHelper.GetParent(scrollViewer) as ScrollViewer;
                if (parentScrollViewer != null)
                {
                    parentScrollViewer.CornerRadius = new CornerRadius(4);
                }
            }

            var itemStyle = new Style(typeof(GridViewItem));
            itemStyle.Setters.Add(
                new Setter(
                    GridViewItem.BorderBrushProperty,
                    Application.Current.Resources["SystemControlTransparentBrush"]
                )
            );
            itemStyle.Setters.Add(
                new Setter(GridViewItem.BorderThicknessProperty, new Thickness(1))
            );
            itemStyle.Setters.Add(new Setter(GridViewItem.MarginProperty, new Thickness(2)));
            itemStyle.Setters.Add(
                new Setter(GridViewItem.CornerRadiusProperty, new CornerRadius(4))
            );
            SymbolGridView.ItemContainerStyle = itemStyle;
#endif
        }

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = FilterTextBox.Text.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(filter))
            {
                SymbolGridView.ItemsSource = _allSymbols;
            }
            else
            {
                SymbolGridView.ItemsSource = _allSymbols
                    .Where(s => s.Entity.ToLowerInvariant().Contains(filter))
                    .ToList();
            }
        }

        private void SymbolGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            _selectedSymbol = e.ClickedItem as SymbolViewModel;
            if (_selectedSymbol != null)
            {
                this.IsPrimaryButtonEnabled = true;
                SymbolGridView.SelectedItem = _selectedSymbol;
            }
        }

        private void ContentDialog_PrimaryButtonClick(
            ContentDialog sender,
            ContentDialogButtonClickEventArgs args
        )
        {
            if (_selectedSymbol != null)
            {
                SelectedEntity = _selectedSymbol.Entity;
            }
            else
            {
                args.Cancel = true;
            }
        }

        private List<SymbolViewModel> GetSymbolList()
        {
            return new List<SymbolViewModel>
            {
                new SymbolViewModel { Character = "Á", Entity = "&Aacute;" },
                new SymbolViewModel { Character = "á", Entity = "&aacute;" },
                new SymbolViewModel { Character = "Â", Entity = "&Acirc;" },
                new SymbolViewModel { Character = "â", Entity = "&acirc;" },
                new SymbolViewModel { Character = "´", Entity = "&acute;" },
                new SymbolViewModel { Character = "Æ", Entity = "&AElig;" },
                new SymbolViewModel { Character = "æ", Entity = "&aelig;" },
                new SymbolViewModel { Character = "À", Entity = "&Agrave;" },
                new SymbolViewModel { Character = "à", Entity = "&agrave;" },
                new SymbolViewModel { Character = "ℵ", Entity = "&alefsym;" },
                new SymbolViewModel { Character = "Α", Entity = "&Alpha;" },
                new SymbolViewModel { Character = "α", Entity = "&alpha;" },
                new SymbolViewModel { Character = "&", Entity = "&amp;" },
                new SymbolViewModel { Character = "∧", Entity = "&and;" },
                new SymbolViewModel { Character = "∠", Entity = "&ang;" },
                new SymbolViewModel { Character = "Å", Entity = "&Aring;" },
                new SymbolViewModel { Character = "å", Entity = "&aring;" },
                new SymbolViewModel { Character = "≈", Entity = "&asymp;" },
                new SymbolViewModel { Character = "Ã", Entity = "&Atilde;" },
                new SymbolViewModel { Character = "ã", Entity = "&atilde;" },
                new SymbolViewModel { Character = "Ä", Entity = "&Auml;" },
                new SymbolViewModel { Character = "ä", Entity = "&auml;" },
                new SymbolViewModel { Character = "„", Entity = "&bdquo;" },
                new SymbolViewModel { Character = "Β", Entity = "&Beta;" },
                new SymbolViewModel { Character = "β", Entity = "&beta;" },
                new SymbolViewModel { Character = "¦", Entity = "&brvbar;" },
                new SymbolViewModel { Character = "•", Entity = "&bull;" },
                new SymbolViewModel { Character = "∩", Entity = "&cap;" },
                new SymbolViewModel { Character = "Ç", Entity = "&Ccedil;" },
                new SymbolViewModel { Character = "ç", Entity = "&ccedil;" },
                new SymbolViewModel { Character = "¸", Entity = "&cedil;" },
                new SymbolViewModel { Character = "¢", Entity = "&cent;" },
                new SymbolViewModel { Character = "Χ", Entity = "&Chi;" },
                new SymbolViewModel { Character = "χ", Entity = "&chi;" },
                new SymbolViewModel { Character = "ˆ", Entity = "&circ;" },
                new SymbolViewModel { Character = "♣", Entity = "&clubs;" },
                new SymbolViewModel { Character = "≅", Entity = "&cong;" },
                new SymbolViewModel { Character = "©", Entity = "&copy;" },
                new SymbolViewModel { Character = "↵", Entity = "&crarr;" },
                new SymbolViewModel { Character = "∪", Entity = "&cup;" },
                new SymbolViewModel { Character = "¤", Entity = "&curren;" },
                new SymbolViewModel { Character = "†", Entity = "&dagger;" },
                new SymbolViewModel { Character = "‡", Entity = "&Dagger;" },
                new SymbolViewModel { Character = "↓", Entity = "&darr;" },
                new SymbolViewModel { Character = "⇓", Entity = "&dArr;" },
                new SymbolViewModel { Character = "°", Entity = "&deg;" },
                new SymbolViewModel { Character = "Δ", Entity = "&Delta;" },
                new SymbolViewModel { Character = "δ", Entity = "&delta;" },
                new SymbolViewModel { Character = "♦", Entity = "&diams;" },
                new SymbolViewModel { Character = "÷", Entity = "&divide;" },
                new SymbolViewModel { Character = "É", Entity = "&Eacute;" },
                new SymbolViewModel { Character = "é", Entity = "&eacute;" },
                new SymbolViewModel { Character = "Ê", Entity = "&Ecirc;" },
                new SymbolViewModel { Character = "ê", Entity = "&ecirc;" },
                new SymbolViewModel { Character = "È", Entity = "&Egrave;" },
                new SymbolViewModel { Character = "è", Entity = "&egrave;" },
                new SymbolViewModel { Character = "∅", Entity = "&empty;" },
                new SymbolViewModel { Character = " ", Entity = "&emsp;" },
                new SymbolViewModel { Character = " ", Entity = "&ensp;" },
                new SymbolViewModel { Character = "Ε", Entity = "&Epsilon;" },
                new SymbolViewModel { Character = "ε", Entity = "&epsilon;" },
                new SymbolViewModel { Character = "≡", Entity = "&equiv;" },
                new SymbolViewModel { Character = "Η", Entity = "&Eta;" },
                new SymbolViewModel { Character = "η", Entity = "&eta;" },
                new SymbolViewModel { Character = "Ð", Entity = "&ETH;" },
                new SymbolViewModel { Character = "ð", Entity = "&eth;" },
                new SymbolViewModel { Character = "Ë", Entity = "&Euml;" },
                new SymbolViewModel { Character = "ë", Entity = "&euml;" },
                new SymbolViewModel { Character = "€", Entity = "&euro;" },
                new SymbolViewModel { Character = "∃", Entity = "&exist;" },
                new SymbolViewModel { Character = "ƒ", Entity = "&fnof;" },
                new SymbolViewModel { Character = "∀", Entity = "&forall;" },
                new SymbolViewModel { Character = "½", Entity = "&frac12;" },
                new SymbolViewModel { Character = "¼", Entity = "&frac14;" },
                new SymbolViewModel { Character = "¾", Entity = "&frac34;" },
                new SymbolViewModel { Character = "⁄", Entity = "&frasl;" },
                new SymbolViewModel { Character = "Γ", Entity = "&Gamma;" },
                new SymbolViewModel { Character = "γ", Entity = "&gamma;" },
                new SymbolViewModel { Character = "≥", Entity = "&ge;" },
                new SymbolViewModel { Character = ">", Entity = "&gt;" },
                new SymbolViewModel { Character = "↔", Entity = "&harr;" },
                new SymbolViewModel { Character = "⇔", Entity = "&hArr;" },
                new SymbolViewModel { Character = "♥", Entity = "&hearts;" },
                new SymbolViewModel { Character = "…", Entity = "&hellip;" },
                new SymbolViewModel { Character = "Í", Entity = "&Iacute;" },
                new SymbolViewModel { Character = "í", Entity = "&iacute;" },
                new SymbolViewModel { Character = "Î", Entity = "&Icirc;" },
                new SymbolViewModel { Character = "î", Entity = "&icirc;" },
                new SymbolViewModel { Character = "¡", Entity = "&iexcl;" },
                new SymbolViewModel { Character = "Ì", Entity = "&Igrave;" },
                new SymbolViewModel { Character = "ì", Entity = "&igrave;" },
                new SymbolViewModel { Character = "ℑ", Entity = "&image;" },
                new SymbolViewModel { Character = "∞", Entity = "&infin;" },
                new SymbolViewModel { Character = "∫", Entity = "&int;" },
                new SymbolViewModel { Character = "Ι", Entity = "&Iota;" },
                new SymbolViewModel { Character = "ι", Entity = "&iota;" },
                new SymbolViewModel { Character = "¿", Entity = "&iquest;" },
                new SymbolViewModel { Character = "∈", Entity = "&isin;" },
                new SymbolViewModel { Character = "Ï", Entity = "&Iuml;" },
                new SymbolViewModel { Character = "ï", Entity = "&iuml;" },
                new SymbolViewModel { Character = "Κ", Entity = "&Kappa;" },
                new SymbolViewModel { Character = "κ", Entity = "&kappa;" },
                new SymbolViewModel { Character = "Λ", Entity = "&Lambda;" },
                new SymbolViewModel { Character = "λ", Entity = "&lambda;" },
                new SymbolViewModel { Character = "⟨", Entity = "&lang;" },
                new SymbolViewModel { Character = "«", Entity = "&laquo;" },
                new SymbolViewModel { Character = "←", Entity = "&larr;" },
                new SymbolViewModel { Character = "⇐", Entity = "&lArr;" },
                new SymbolViewModel { Character = "⌈", Entity = "&lceil;" },
                new SymbolViewModel { Character = "“", Entity = "&ldquo;" },
                new SymbolViewModel { Character = "≤", Entity = "&le;" },
                new SymbolViewModel { Character = "⌊", Entity = "&lfloor;" },
                new SymbolViewModel { Character = "∗", Entity = "&lowast;" },
                new SymbolViewModel { Character = "◊", Entity = "&loz;" },
                new SymbolViewModel { Character = "‎", Entity = "&lrm;" },
                new SymbolViewModel { Character = "‹", Entity = "&lsaquo;" },
                new SymbolViewModel { Character = "‘", Entity = "&lsquo;" },
                new SymbolViewModel { Character = "<", Entity = "&lt;" },
                new SymbolViewModel { Character = "¯", Entity = "&macr;" },
                new SymbolViewModel { Character = "—", Entity = "&mdash;" },
                new SymbolViewModel { Character = "µ", Entity = "&micro;" },
                new SymbolViewModel { Character = "·", Entity = "&middot;" },
                new SymbolViewModel { Character = "−", Entity = "&minus;" },
                new SymbolViewModel { Character = "Μ", Entity = "&Mu;" },
                new SymbolViewModel { Character = "μ", Entity = "&mu;" },
                new SymbolViewModel { Character = "∇", Entity = "&nabla;" },
                new SymbolViewModel { Character = " ", Entity = "&nbsp;" },
                new SymbolViewModel { Character = "–", Entity = "&ndash;" },
                new SymbolViewModel { Character = "≠", Entity = "&ne;" },
                new SymbolViewModel { Character = "∋", Entity = "&ni;" },
                new SymbolViewModel { Character = "¬", Entity = "&not;" },
                new SymbolViewModel { Character = "∉", Entity = "&notin;" },
                new SymbolViewModel { Character = "⊄", Entity = "&nsub;" },
                new SymbolViewModel { Character = "Ñ", Entity = "&Ntilde;" },
                new SymbolViewModel { Character = "ñ", Entity = "&ntilde;" },
                new SymbolViewModel { Character = "Ν", Entity = "&Nu;" },
                new SymbolViewModel { Character = "ν", Entity = "&nu;" },
                new SymbolViewModel { Character = "Ó", Entity = "&Oacute;" },
                new SymbolViewModel { Character = "ó", Entity = "&oacute;" },
                new SymbolViewModel { Character = "Ô", Entity = "&Ocirc;" },
                new SymbolViewModel { Character = "ô", Entity = "&ocirc;" },
                new SymbolViewModel { Character = "Œ", Entity = "&OElig;" },
                new SymbolViewModel { Character = "œ", Entity = "&oelig;" },
                new SymbolViewModel { Character = "Ò", Entity = "&Ograve;" },
                new SymbolViewModel { Character = "ò", Entity = "&ograve;" },
                new SymbolViewModel { Character = "‾", Entity = "&oline;" },
                new SymbolViewModel { Character = "Ω", Entity = "&Omega;" },
                new SymbolViewModel { Character = "ω", Entity = "&omega;" },
                new SymbolViewModel { Character = "Ο", Entity = "&Omicron;" },
                new SymbolViewModel { Character = "ο", Entity = "&omicron;" },
                new SymbolViewModel { Character = "⊕", Entity = "&oplus;" },
                new SymbolViewModel { Character = "∨", Entity = "&or;" },
                new SymbolViewModel { Character = "ª", Entity = "&ordf;" },
                new SymbolViewModel { Character = "º", Entity = "&ordm;" },
                new SymbolViewModel { Character = "Ø", Entity = "&Oslash;" },
                new SymbolViewModel { Character = "ø", Entity = "&oslash;" },
                new SymbolViewModel { Character = "Õ", Entity = "&Otilde;" },
                new SymbolViewModel { Character = "õ", Entity = "&otilde;" },
                new SymbolViewModel { Character = "⊗", Entity = "&otimes;" },
                new SymbolViewModel { Character = "Ö", Entity = "&Ouml;" },
                new SymbolViewModel { Character = "ö", Entity = "&ouml;" },
                new SymbolViewModel { Character = "¶", Entity = "&para;" },
                new SymbolViewModel { Character = "∂", Entity = "&part;" },
                new SymbolViewModel { Character = "‰", Entity = "&permil;" },
                new SymbolViewModel { Character = "⊥", Entity = "&perp;" },
                new SymbolViewModel { Character = "Φ", Entity = "&Phi;" },
                new SymbolViewModel { Character = "φ", Entity = "&phi;" },
                new SymbolViewModel { Character = "Π", Entity = "&Pi;" },
                new SymbolViewModel { Character = "π", Entity = "&pi;" },
                new SymbolViewModel { Character = "ϖ", Entity = "&piv;" },
                new SymbolViewModel { Character = "±", Entity = "&plusmn;" },
                new SymbolViewModel { Character = "£", Entity = "&pound;" },
                new SymbolViewModel { Character = "′", Entity = "&prime;" },
                new SymbolViewModel { Character = "″", Entity = "&Prime;" },
                new SymbolViewModel { Character = "∏", Entity = "&prod;" },
                new SymbolViewModel { Character = "∝", Entity = "&prop;" },
                new SymbolViewModel { Character = "Ψ", Entity = "&Psi;" },
                new SymbolViewModel { Character = "ψ", Entity = "&psi;" },
                new SymbolViewModel { Character = "\"", Entity = "&quot;" },
                new SymbolViewModel { Character = "√", Entity = "&radic;" },
                new SymbolViewModel { Character = "⟩", Entity = "&rang;" },
                new SymbolViewModel { Character = "»", Entity = "&raquo;" },
                new SymbolViewModel { Character = "→", Entity = "&rarr;" },
                new SymbolViewModel { Character = "⇒", Entity = "&rArr;" },
                new SymbolViewModel { Character = "⌉", Entity = "&rceil;" },
                new SymbolViewModel { Character = "”", Entity = "&rdquo;" },
                new SymbolViewModel { Character = "ℜ", Entity = "&real;" },
                new SymbolViewModel { Character = "®", Entity = "&reg;" },
                new SymbolViewModel { Character = "⌋", Entity = "&rfloor;" },
                new SymbolViewModel { Character = "Ρ", Entity = "&Rho;" },
                new SymbolViewModel { Character = "ρ", Entity = "&rho;" },
                new SymbolViewModel { Character = "‏", Entity = "&rlm;" },
                new SymbolViewModel { Character = "›", Entity = "&rsaquo;" },
                new SymbolViewModel { Character = "’", Entity = "&rsquo;" },
                new SymbolViewModel { Character = "‚", Entity = "&sbquo;" },
                new SymbolViewModel { Character = "Š", Entity = "&Scaron;" },
                new SymbolViewModel { Character = "š", Entity = "&scaron;" },
                new SymbolViewModel { Character = "⋅", Entity = "&sdot;" },
                new SymbolViewModel { Character = "§", Entity = "&sect;" },
                new SymbolViewModel { Character = "­", Entity = "&shy;" },
                new SymbolViewModel { Character = "Σ", Entity = "&Sigma;" },
                new SymbolViewModel { Character = "σ", Entity = "&sigma;" },
                new SymbolViewModel { Character = "ς", Entity = "&sigmaf;" },
                new SymbolViewModel { Character = "∼", Entity = "&sim;" },
                new SymbolViewModel { Character = "♠", Entity = "&spades;" },
                new SymbolViewModel { Character = "⊂", Entity = "&sub;" },
                new SymbolViewModel { Character = "⊆", Entity = "&sube;" },
                new SymbolViewModel { Character = "∑", Entity = "&sum;" },
                new SymbolViewModel { Character = "⊃", Entity = "&sup;" },
                new SymbolViewModel { Character = "¹", Entity = "&sup1;" },
                new SymbolViewModel { Character = "²", Entity = "&sup2;" },
                new SymbolViewModel { Character = "³", Entity = "&sup3;" },
                new SymbolViewModel { Character = "⊇", Entity = "&supe;" },
                new SymbolViewModel { Character = "ß", Entity = "&szlig;" },
                new SymbolViewModel { Character = "Τ", Entity = "&Tau;" },
                new SymbolViewModel { Character = "τ", Entity = "&tau;" },
                new SymbolViewModel { Character = "∴", Entity = "&there4;" },
                new SymbolViewModel { Character = "Θ", Entity = "&Theta;" },
                new SymbolViewModel { Character = "θ", Entity = "&theta;" },
                new SymbolViewModel { Character = "ϑ", Entity = "&thetasym;" },
                new SymbolViewModel { Character = " ", Entity = "&thinsp;" },
                new SymbolViewModel { Character = "Þ", Entity = "&THORN;" },
                new SymbolViewModel { Character = "þ", Entity = "&thorn;" },
                new SymbolViewModel { Character = "˜", Entity = "&tilde;" },
                new SymbolViewModel { Character = "×", Entity = "&times;" },
                new SymbolViewModel { Character = "™", Entity = "&trade;" },
                new SymbolViewModel { Character = "Ú", Entity = "&Uacute;" },
                new SymbolViewModel { Character = "ú", Entity = "&uacute;" },
                new SymbolViewModel { Character = "↑", Entity = "&uarr;" },
                new SymbolViewModel { Character = "⇑", Entity = "&uArr;" },
                new SymbolViewModel { Character = "Û", Entity = "&Ucirc;" },
                new SymbolViewModel { Character = "û", Entity = "&ucirc;" },
                new SymbolViewModel { Character = "Ù", Entity = "&Ugrave;" },
                new SymbolViewModel { Character = "ù", Entity = "&ugrave;" },
                new SymbolViewModel { Character = "¨", Entity = "&uml;" },
                new SymbolViewModel { Character = "ϒ", Entity = "&upsih;" },
                new SymbolViewModel { Character = "Υ", Entity = "&Upsilon;" },
                new SymbolViewModel { Character = "υ", Entity = "&upsilon;" },
                new SymbolViewModel { Character = "Ü", Entity = "&Uuml;" },
                new SymbolViewModel { Character = "ü", Entity = "&uuml;" },
                new SymbolViewModel { Character = "℘", Entity = "&weierp;" },
                new SymbolViewModel { Character = "Ξ", Entity = "&Xi;" },
                new SymbolViewModel { Character = "ξ", Entity = "&xi;" },
                new SymbolViewModel { Character = "Ý", Entity = "&Yacute;" },
                new SymbolViewModel { Character = "ý", Entity = "&yacute;" },
                new SymbolViewModel { Character = "¥", Entity = "&yen;" },
                new SymbolViewModel { Character = "ÿ", Entity = "&yuml;" },
                new SymbolViewModel { Character = "Ÿ", Entity = "&Yuml;" },
                new SymbolViewModel { Character = "Ζ", Entity = "&Zeta;" },
                new SymbolViewModel { Character = "ζ", Entity = "&zeta;" },
                new SymbolViewModel { Character = "‍", Entity = "&zwj;" },
                new SymbolViewModel { Character = "‌", Entity = "&zwnj;" },
            };
        }
    }
}
