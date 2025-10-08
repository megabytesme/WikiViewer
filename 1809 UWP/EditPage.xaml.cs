using System;
using System.Threading.Tasks;
using WikiViewer.Shared.Uwp.Controls;
using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP.Pages
{
    public sealed partial class EditPage : EditPageBase
    {
        public EditPage()
        {
            this.InitializeComponent();
            AttachEditorFunctionality();
        }

        #region Page Setup & Overrides
        protected override RichEditBox WikitextEditorTextBox => WikitextEditor;
        protected override TextBlock PageTitleTextBlock => PageTitle;
        protected override TextBlock LoadingTextBlock => LoadingText;
        protected override Grid LoadingOverlayGrid => LoadingOverlay;
        protected override Grid SplitterGrid => SplitGrid;
        protected override Button SaveAppBarButton => (AppBarButton)SaveButton;
        protected override Button PreviewAppBarButton => (AppBarButton)PreviewButton;

        protected override async Task ShowPreview(string htmlContent)
        {
            try
            {
                await PreviewWebView.EnsureCoreWebView2Async();
                PreviewWebView.NavigateToString(htmlContent);
                PreviewPlaceholder.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                PreviewWebView.Visibility = Windows.UI.Xaml.Visibility.Visible;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[WebView2] Failed to ensure core or navigate: {ex.Message}"
                );
                HidePreview($"Failed to generate preview: {ex.Message}");
            }
        }

        protected override void HidePreview(string placeholderText)
        {
            PreviewPlaceholder.Text = placeholderText;
            PreviewWebView.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
        }

        protected override void ResetPreviewPaneVisually()
        {
            PreviewWebView.Visibility = Visibility.Collapsed;

            if (PreviewPlaceholder != null)
            {
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                PreviewPlaceholder.Text = "Click 'Preview' to see a rendering of your changes.";
            }
        }

        private void Cut_Click(object sender, RoutedEventArgs e)
        {
            WikitextEditor.Document.Selection.Cut();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            WikitextEditor.Document.Selection.Copy();
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            WikitextEditor.Document.Selection.Paste(0);
        }

        #endregion

        #region --- Formatting ---

        private void InsertBold_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("'''", "'''", "bold text");

        private void InsertItalic_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("''", "''", "italic text");

        private void InsertBoldItalic_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("'''''", "'''''", "bold & italic text");

        private void InsertUnderline_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<u>", "</u>", "underlined text");

        private void InsertStrikethrough_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<s>", "</s>", "strikethrough text");

        private void InsertInserted_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<ins>", "</ins>", "inserted text");

        private void InsertDeleted_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<del>", "</del>", "deleted text");

        private void InsertSuperscript_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<sup>", "</sup>", "superscript");

        private void InsertSubscript_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<sub>", "</sub>", "subscript");

        private void InsertSmall_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<small>", "</small>", "small text");

        private void InsertBig_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<big>", "</big>", "big text");

        private void InsertSmallCaps_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{smallcaps|", "}}", "Small Caps Text");

        private void InsertCodeInline_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<code>", "</code>", "inline code");

        private void InsertSyntaxHighlight_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext(
                "<syntaxhighlight lang=\"csharp\">",
                "</syntaxhighlight>",
                "Console.WriteLine(\"Hello, World!\");"
            );

        private void InsertPreformatted_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<pre>", "</pre>", "preformatted text, ignores wiki markup");

        private void InsertColoredText_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{color|blue|", "}}", "blue text");

        private void InsertHighlightedText_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{Font color||yellow|", "}}", "highlighted text");

        private void InsertNBSP_Click(object sender, RoutedEventArgs e) => InsertWikitext("&nbsp;");

        private void InsertNoWrap_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{nowrap|", "}}", "text that will not wrap");

        #endregion

        #region --- Layout & Structure ---

        private void InsertHeading2_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n== ", " ==\n", "Heading Level 2");

        private void InsertHeading3_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n=== ", " ===\n", "Heading Level 3");

        private void InsertHeading4_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n==== ", " ====\n", "Heading Level 4");

        private void InsertHeading5_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n===== ", " =====\n", "Heading Level 5");

        private void InsertHeading6_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n====== ", " ======\n", "Heading Level 6");

        private void InsertBulletedListItem_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n* ", "", "List item");

        private void InsertNumberedListItem_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n# ", "", "List item");

        private void InsertIndentedListItem_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n: ", "", "Indented text (for talk pages)");

        private void InsertDefinitionTerm_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n; ", "", "Term");

        private void InsertDefinitionData_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n: ", "", "Definition");

        private void InsertTableBasic_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext(
                "\n{| class=\"wikitable\"\n|-\n| Cell 1 || Cell 2\n|-\n| Cell 3 || Cell 4\n|}\n",
                selectDefaultText: false
            );

        private void InsertTableWithHeader_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext(
                "\n{| class=\"wikitable\"\n! Header 1 !! Header 2\n|-\n| Cell 1 || Cell 2\n|-\n| Cell 3 || Cell 4\n|}\n",
                selectDefaultText: false
            );

        private void InsertTableSortable_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext(
                "\n{| class=\"wikitable sortable\"\n! Header 1 !! Header 2\n|-\n| Cell 1 || Cell 2\n|-\n| Cell 3 || Cell 4\n|}\n",
                selectDefaultText: false
            );

        private void InsertBlockquote_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n<blockquote>\n", "\n</blockquote>\n", "Quoted text.");

        private void InsertCenter_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{center|", "}}", "Centered Text");

        private void InsertAlignRight_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<div style=\"text-align: right;\">", "</div>", "Right-aligned text");

        private void InsertFloatRight_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<div class=\"floatright\">", "</div>", "Floating block on the right");

        private void InsertHorizontalRule_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n----\n");

        private void InsertLineBreak_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<br />");

        private void InsertClear_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n{{clear}}\n");

        #endregion

        #region --- Links & Images ---

        private void InsertInternalLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[", "]]", "Page title");

        private void InsertSectionLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[", "]]", "Page title#Section title|display text");

        private void InsertExternalLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[", "]", "http://www.example.com Link text");

        private void InsertCategory_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n[[Category:", "]]\n", "Category name");

        private void InsertCategoryLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[:Category:", "]]", "Category name");

        private void InsertRedirect_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("#REDIRECT [[", "]]", "Target Page");

        private void InsertInterlanguageLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[", "]]", "es:Nombre del artículo");

        private void InsertInterwikiLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[", "]]", "wiktionary:fr:bonjour|bonjour");

        private void InsertImageBasic_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[File:", "]]", "Example.jpg");

        private void InsertImageThumbnail_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[File:", "|thumb|Caption text]]", "Example.jpg");

        private void InsertImageAlign_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[File:", "|thumb|left|Caption text]]", "Example.jpg");

        private void InsertImageResized_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[File:", "|100px]]", "Example.jpg");

        private void InsertGallery_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext(
                "\n<gallery>\n",
                "\n</gallery>\n",
                "File:Example.jpg|Caption 1\nFile:Example.jpg|Caption 2"
            );

        private void InsertMediaLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[Media:", "|Link text]]", "Example.ogg");

        private void InsertFileLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[:File:", "]]", "Example.jpg");

        private void InsertEditLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[{{fullurl:", "|action=edit}} edit link]", "PAGENAME");

        private void InsertRevisionLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[Special:Permalink/", "|link text]]", "123456789");

        private void InsertDiffLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[Special:Diff/", "|link text]]", "123456789/987654321");

        private void InsertWhatLinksHereLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[Special:WhatLinksHere/", "]]", "Page Name");

        private void InsertUserContribsLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[Special:Contributions/", "]]", "Username");

        #endregion

        #region --- Citations & References ---

        private void InsertReference_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<ref>", "</ref>", "Your source here");

        private void InsertReferencesList_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n== References ==\n<references />\n");

        private void InsertCiteWeb_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext(
                "{{cite web |url=",
                " |title= |author= |date= |website= |publisher= |access-date=}}",
                "http://example.com"
            );

        private void InsertCiteBook_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext(
                "{{cite book |last=",
                " |first= |title= |date= |publisher= |isbn= |pages=}}",
                "AuthorLast"
            );

        private void InsertCiteJournal_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext(
                "{{cite journal |last=",
                " |first= |date= |title= |journal= |volume= |issue= |pages= |doi=}}",
                "AuthorLast"
            );

        private void InsertCitationNeeded_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{Citation needed|date=", "}}", DateTime.Now.ToString("MMMM yyyy"));

        #endregion

        #region --- Advanced Inserts ---

        private void InsertTemplate_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{", "}}", "template_name");

        private void InsertTemplateSubst_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{subst:", "}}", "template_name");

        private void InsertTemplateSafeSubst_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{safesubst:", "}}", "template_name");

        private void InsertNoInclude_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<noinclude>", "</noinclude>", "documentation or categories");

        private void InsertIncludeOnly_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<includeonly>", "</includeonly>", "transcluded content only");

        private void InsertOnlyInclude_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext(
                "<onlyinclude>",
                "</onlyinclude>",
                "this is the only part that will be transcluded"
            );

        private void InsertMath_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<math>", "</math>", "E=mc^2");

        private void InsertPoem_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n<poem>\n", "\n</poem>\n", "Poetic text here.");

        private void InsertScore_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<score>", "</score>", "\\relative c' { c d e f | g a b c }");

        private void InsertHieroglyphs_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<hiero>", "</hiero>", "A1");

        private void InsertNoWiki_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<nowiki>", "</nowiki>", "Text with [[wikimarkup]] to ignore");

        private void InsertMagicWordFORCETOC_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("__FORCETOC__");

        private void InsertMagicWordTOC_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("__TOC__");

        private void InsertMagicWordNOTOC_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("__NOTOC__");

        private void InsertMagicWordPAGENAME_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{PAGENAME}}");

        private void InsertVariableNAMESPACE_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{NAMESPACE}}");

        private void InsertVariableREVISIONID_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{REVISIONID}}");

        private void InsertVariableREVISIONUSER_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{REVISIONUSER}}");

        private void InsertVariableSITENAME_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{SITENAME}}");

        private void InsertVariableNUMBEROFARTICLES_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{NUMBEROFARTICLES}}");

        private void InsertVariableNUMBEROFUSERS_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{NUMBEROFUSERS}}");

        private void InsertVariableCURRENTYEAR_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{CURRENTYEAR}}");

        private void InsertVariableCURRENTMONTHNAME_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{CURRENTMONTHNAME}}");

        private void InsertVariableCURRENTDAY_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{CURRENTDAY}}");

        private void InsertVariableCURRENTDAYNAME_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{CURRENTDAYNAME}}");

        private void InsertVariableCURRENTTIME_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{CURRENTTIME}}");

        private void InsertComment_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<!-- ", " -->", "hidden comment");

        private async void InsertSpecialCharacter_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SymbolPickerDialog();
            var result = await dialog.ShowAsync();
            if (
                result == ContentDialogResult.Primary
                && !string.IsNullOrEmpty(dialog.SelectedEntity)
            )
            {
                InsertWikitext(dialog.SelectedEntity);
            }
        }

        #endregion

        #region --- Signature ---

        private void InsertSignature3_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("~~~");

        private void InsertSignature4_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("~~~~");

        private void InsertSignature5_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("~~~~~");

        #endregion
    }
}
