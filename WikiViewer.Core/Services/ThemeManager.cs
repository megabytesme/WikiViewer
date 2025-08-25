using System;
using System.Threading.Tasks;
using Windows.Storage;

namespace WikiViewer.Core.Services
{
    public static class ThemeManager
    {
        public const string ThemeCssFileName = "theme.css";

        private static string GetDefaultThemeCss()
        {
            return @"
            :root {
                 --text-primary: #000000;
                 --text-secondary: #5c5c5c;
                 --text-disabled: #a0a0a0;
                 --link-color: #0058e0;
                 --link-visited-color: #551a8b;
                 --divider-color: rgba(0, 0, 0, 0.08);
                 --card-background: #f9f9f9;
                 --card-border: #e1e1e1;
                 --card-shadow: rgba(0, 0, 0, 0.05);
                 --card-header-background: rgba(0, 0, 0, 0.03);
                 --card-hover-background: rgba(0, 0, 0, 0.05);
                 --ambox-notice-border: #36c;
                 --ambox-warning-border: #f28500;
                 --ambox-danger-border: #b32424;
            }
             @media (prefers-color-scheme: dark) {
                 :root {
                     --text-primary: #ffffff;
                     --text-secondary: #b0b0b0;
                     --text-disabled: #5f5f5f;
                     --link-color: #61a6ff;
                     --link-visited-color: #c199ff;
                     --divider-color: rgba(255, 255, 255, 0.1);
                     --card-background: #2c2c2c;
                     --card-border: #444444;
                     --card-shadow: rgba(0, 0, 0, 0.2);
                     --card-header-background: rgba(255, 255, 255, 0.05);
                     --card-hover-background: rgba(255, 255, 255, 0.07);
                     --ambox-notice-border: #61a6ff;
                     --ambox-warning-border: #ff9b38;
                     --ambox-danger-border: #ff5c5c;
                }
            }
             html, body {
                 background-color: transparent !important;
                 color: var(--text-primary);
                 font-family: 'Segoe UI Variable', 'Segoe UI', sans-serif;
                 margin: 0;
                 padding: 0;
                 font-size: 16px;
                 -webkit-font-smoothing: antialiased;
            }
             .fluent-enabled html, .fluent-enabled body {
                 -webkit-backdrop-filter: blur(10px) saturate(1.8);
                 backdrop-filter: blur(10px) saturate(1.8);
                 background-color: rgba(255, 255, 255, 0.3) !important;
            }
             @media (prefers-color-scheme: dark) {
                 .fluent-enabled html, .fluent-enabled body {
                     background-color: rgba(30, 30, 30, 0.3) !important;
                }
            }
             .mw-parser-output {
                 padding: 24px;
                 box-sizing: border-box;
            }
             a {
                 color: var(--link-color);
                 text-decoration: none;
                 transition: color 0.15s ease-in-out;
            }
             a:visited {
                 color: var(--link-visited-color);
            }
             a:hover {
                 text-decoration: underline;
            }
             a.new {
                 color: var(--ambox-danger-border) !important;
                 font-style: italic;
            }
             a.selflink {
                 color: var(--text-secondary);
                 pointer-events: none;
                 text-decoration: none;
            }
             h1, h2, h3, h4, h5, h6 {
                 border-bottom: 1px solid var(--divider-color);
                 padding-bottom: 8px;
                 margin-top: 32px;
                 font-weight: 600;
                 color: var(--text-primary);
            }
             h1 {
                 font-size: 2.2em;
            }
             h2 {
                 font-size: 1.8em;
            }
             h3 {
                 font-size: 1.5em;
            }
             h4 {
                 font-size: 1.2em;
            }
             h5 {
                 font-size: 1em;
            }
             h6 {
                 font-size: 0.9em;
            }
             p {
                 line-height: 1.6;
                 color: var(--text-primary);
            }
             img {
                 max-width: 100%;
                 height: auto;
                 border-radius: 6px;
            }
             hr {
                 border: none;
                 border-top: 1px solid var(--divider-color);
                 margin: 2em 0;
            }
             ul, ol {
                 color: var(--text-primary);
            }
             blockquote {
                 margin-left: 16px;
                 padding-left: 16px;
                 border-left: 3px solid var(--divider-color);
                 color: var(--text-secondary);
                 font-style: italic;
            }
             code, pre {
                 background-color: var(--card-header-background);
                 border-radius: 4px;
                 padding: 2px 5px;
                 font-family: 'Segoe UI Mono', 'Consolas', monospace;
                 font-size: 0.9em;
                 color: var(--text-primary);
            }
             pre {
                 padding: 16px;
                 overflow-x: auto;
                 white-space: pre-wrap;
                 word-wrap: break-word;
            }
             .mw-editsection, .mw-jump-link, .noprint, .mw-content-ltr .shortdescription {
                 display: none !important;
            }
             .infobox, table.wikitable, .navbox, .ambox, .box-Multiple_issues, .side-box, #mp-topbanner, #mp-left, #mp-right, #mp-middle, #mp-lower, #mp-bottom, .bar-chart, ombox {
                 background-color: var(--card-background) !important;
                 border: 1px solid var(--card-border) !important;
                 border-radius: 8px !important;
                 box-shadow: 0 4px 12px var(--card-shadow) !important;
                 margin-bottom: 16px !important;
                 overflow: hidden !important;
                 padding: 16px !important;
                 box-sizing: border-box !important;
            }
             .fluent-enabled .infobox, .fluent-enabled table.wikitable, .fluent-enabled .navbox, .fluent-enabled .ambox, .fluent-enabled .box-Multiple_issues, .fluent-enabled .side-box, .fluent-enabled #mp-topbanner, .fluent-enabled #mp-left, .fluent-enabled #mp-right, .fluent-enabled #mp-middle, .fluent-enabled #mp-lower, .fluent-enabled #mp-bottom, .fluent-enabled .bar-chart, .fluent-enabled .ombox {
                 -webkit-backdrop-filter: blur(20px) saturate(1.8) !important;
                 backdrop-filter: blur(20px) saturate(1.8) !important;
                 background-color: rgba(245, 245, 245, 0.75) !important;
                 border-color: rgba(0, 0, 0, 0.08) !important;
            }
             @media (prefers-color-scheme: dark) {
                 .fluent-enabled .infobox, .fluent-enabled table.wikitable, .fluent-enabled .navbox, .fluent-enabled .ambox, .fluent-enabled .box-Multiple_issues, .fluent-enabled .side-box, .fluent-enabled #mp-topbanner, .fluent-enabled #mp-left, .fluent-enabled #mp-right, .fluent-enabled #mp-middle, .fluent-enabled #mp-lower, .fluent-enabled #mp-bottom, .fluent-enabled .ombox {
                     background-color: rgba(44, 44, 44, 0.7) !important;
                     border-color: rgba(255, 255, 255, 0.1) !important;
                }
            }
             table.wikitable {
                 width: 100%;
                 border-collapse: separate;
                 border-spacing: 0;
            }
             .infobox>tbody>tr>th, .wikitable>tbody>tr>th, .navbox-title, .navbox-group, .infobox-above {
                 font-weight: 600 !important;
                 color: var(--text-secondary) !important;
                 background-color: var(--card-header-background) !important;
                 padding: 12px 16px !important;
                 text-align: left !important;
                 border: none !important;
                 vertical-align: top !important;
            }
             .infobox>tbody>tr>td, .wikitable>tbody>tr>td {
                 padding: 12px 16px;
                 text-align: left;
                 border: none;
                 vertical-align: top;
                 color: var(--text-primary);
            }
             .infobox>tbody>tr:not(:last-child)>*, .wikitable>tbody>tr:not(:last-child)>* {
                 border-bottom: 1px solid var(--divider-color);
            }
             .infobox-caption {
                 font-weight: bold;
                 font-size: 1.1em;
                 padding: 12px 16px;
                 text-align: center;
                 color: var(--text-primary);
            }
             .wikitable .table-yes2, .wikitable .table-no2 {
                 color: var(--text-primary) !important;
            }
             .wikitable .table-yes2 {
                 background-color: rgba(158, 255, 158, 0.5) !important;
            }
             .wikitable .table-no2 {
                 background-color: rgba(255, 227, 227, 0.6) !important;
            }
             @media (prefers-color-scheme: dark) {
                 .wikitable .table-yes2 {
                     background-color: rgba(34, 197, 94, 0.3) !important;
                }
                 .wikitable .table-no2 {
                     background-color: rgba(239, 68, 68, 0.25) !important;
                }
            }
             .navbox-inner {
                 border-collapse: collapse;
                 border-spacing: 0;
                 background-color: transparent !important;
                 width: 100%;
            }
             .navbox-title, .navbox-abovebelow, .navbox-group, .navbox-subgroup .navbox-title, .navbox-subgroup .navbox-group, .navbox-even, .navbox-odd, .navbox-subgroup {
                 background-color: transparent !important;
                 color: var(--text-primary);
            }
             .navbox-title {
                 background-color: var(--card-header-background) !important;
                 border-bottom: 1px solid var(--divider-color);
                 font-size: 1.1em;
                 font-weight: 600;
            }
             .navbox-group {
                 background-color: var(--card-header-background) !important;
                 border-top: 1px solid var(--divider-color);
                 font-size: 0.8em;
                 font-weight: 600;
                 text-transform: uppercase;
                 text-align: center;
                 white-space: normal;
                 border-right: 1px solid var(--divider-color);
            }
             tr+tr>.navbox-abovebelow, tr+tr>.navbox-group, tr+tr>.navbox-image, tr+tr>.navbox-list {
                 border-top: none;
            }
             .navbox-list {
                 padding: 8px 0;
            }
             .navbox-list li a {
                 display: inline-block;
                 padding: 4px 8px;
                 border-radius: 4px;
                 transition: background-color 0.15s ease-in-out;
                 color: var(--link-color);
            }
             .navbox-list li a:hover {
                 background: var(--card-hover-background);
                 text-decoration: none;
            }
             .reflist {
                 font-size: 0.9em;
                 column-width: 32em;
                 column-gap: 2em;
                 margin-top: 1em;
                 border-top: 1px solid var(--divider-color);
                 padding-top: 1em;
                 color: var(--text-primary);
            }
             .reflist li {
                 margin-bottom: 0.75em;
            }
             .reflist .reference-text {
                 color: var(--text-secondary);
            }
             .ambox {
                 border-left-width: 8px;
                 border-left-style: solid;
                 padding: 16px;
                 color: var(--text-primary);
            }
             .ambox .mbox-text {
                 color: var(--text-primary);
            }
             .ambox-content, .box-BLP_one_source, .box-BLP_primary_sources {
                 border-left-color: var(--ambox-warning-border);
            }
             .ambox-speedy, .ambox-delete {
                 border-left-color: var(--ambox-danger-border);
            }
             .ambox-style, .ambox-protection, .ambox-move {
                 border-left-color: var(--ambox-notice-border);
            }
             .hatnote {
                 font-style: italic;
                 color: var(--text-secondary);
                 padding: 8px 16px;
                 margin-bottom: 16px;
                 border-left: 3px solid var(--divider-color);
            }
             .side-box {
                 padding: 16px;
                 box-sizing: border-box;
            }
             .side-box .side-box-flex {
                 display: flex;
                 align-items: center;
                 gap: 12px;
            }
             .side-box .side-box-image {
                 flex-shrink: 0;
                 display: flex;
                 align-items: center;
                 justify-content: center;
                 min-width: 40px;
            }
             .side-box .side-box-image img {
                 border-radius: 4px;
                 max-height: 40px;
                 width: auto;
            }
             .side-box .side-box-text {
                 flex-grow: 1;
                 color: var(--text-primary);
                 font-size: 0.95em;
                 line-height: 1.4;
            }
             .side-box .side-box-text a {
                 color: var(--link-color);
                 font-weight: bold;
                 text-decoration: none;
            }
             .side-box .side-box-text a:hover {
                 text-decoration: underline;
            }
             .side-box .plainlist ul, .side-box .plainlist ol {
                 margin: 0;
                 padding: 0;
                 list-style: none;
            }
             .side-box .plainlist li {
                 margin: 0;
                 padding: 0;
                 display: inline;
            }
             #mp-topbanner {
                 text-align: center;
                 padding: 48px 24px;
                 margin-bottom: 24px;
                 border: 1px solid var(--card-border);
                 border-radius: 8px;
                 background-color: var(--card-background);
            }
             .fluent-enabled #mp-topbanner {
                 -webkit-backdrop-filter: blur(20px) saturate(1.8);
                 backdrop-filter: blur(20px) saturate(1.8);
                 background-color: rgba(245, 245, 245, 0.5);
                 border-color: rgba(0, 0, 0, 0.08);
            }
             @media (prefers-color-scheme: dark) {
                 .fluent-enabled #mp-topbanner {
                     background-color: rgba(44, 44, 44, 0.5);
                     border-color: rgba(255, 255, 255, 0.1);
                }
            }
             #mp-welcome h1 {
                 font-size: 3em;
                 font-weight: 700;
                 border-bottom: none;
                 margin: 0;
                 padding: 0;
                 color: var(--text-primary);
            }
             #mp-welcome h1 a {
                 color: inherit;
                 text-decoration: none;
            }
             #mp-welcome h1 a:hover {
                 text-decoration: underline;
            }
             #mp-free {
                 font-size: 1.2em;
                 color: var(--text-secondary);
                 margin-top: 8px;
            }
             #articlecount {
                 margin-top: 16px;
                 font-size: 0.9em;
                 color: var(--text-secondary);
            }
             #articlecount ul {
                 list-style: none;
                 padding: 0;
                 margin: 0;
            }
             #articlecount li {
                 display: inline;
            }
             #articlecount li:not(:last-child)::after {
                 content: ' · ';
                 font-weight: bold;
                 margin: 0 0.5em;
            }
             .mp-h2 {
                 color: var(--text-primary) !important;
                 background-color: var(--card-header-background) !important;
                 border: 1px solid var(--card-border) !important;
                 border-radius: 8px !important;
                 padding: 10px 16px !important;
                 margin-bottom: 16px !important;
                 box-shadow: 0 2px 5px rgba(0, 0, 0, 0.03) !important;
                 text-align: left !important;
                 font-size: 1.2em !important;
                 font-weight: 600 !important;
            }
             #mp-left, #mp-right, #mp-middle, #mp-lower, #mp-bottom {
                 background-color: var(--card-background);
            }
             .mp-thumb .thumbinner {
                 background: transparent !important;
                 color: inherit !important;
                 border: none !important;
                 padding: 0 !important;
            }
             .wikipedia-languages-count-container {
                 color: var(--text-secondary);
            }
             .wikipedia-languages-prettybars {
                 background-color: var(--divider-color);
            }
             table[cellpadding='2'][style*='width:100%'], table[cellpadding='2'][style*='width:100%']>tbody>tr>td {
                 background-color: var(--card-background) !important;
                 border-radius: 8px;
                 box-shadow: 0 4px 12px var(--card-shadow);
                 margin-bottom: 16px;
                 padding: 0;
                 border-collapse: separate;
                 border-spacing: 0;
                 overflow: hidden;
                 color: var(--text-primary);
            }
             table[cellpadding='2'][style*='width:100%']>tbody>tr>td {
                 padding: 10px 15px !important;
                 border: none !important;
                 vertical-align: top;
                 color: var(--text-primary);
            }
             table[cellpadding='2'][style*='width:100%']>tbody>tr>td[style*='background:white'] {
                 background-color: var(--card-header-background) !important;
            }
             table[cellpadding='2'][style*='width:100%'] a {
                 color: var(--link-color);
                 text-decoration: none;
            }
             table[cellpadding='2'][style*='width:100%'] a:hover {
                 text-decoration: underline;
            }
             table[cellpadding='2'][style*='width:100%'] b {
                 color: var(--text-primary);
            }
             table[cellpadding='2'][style*='width:100%'] ul, table[cellpadding='2'][style*='width:100%'] li {
                 color: var(--text-secondary);
                 font-size: 0.9em;
            }
             .fluent-enabled table[cellpadding='2'][style*='width:100%'] {
                 -webkit-backdrop-filter: blur(20px) saturate(1.8);
                 backdrop-filter: blur(20px) saturate(1.8);
                 background-color: rgba(245, 245, 245, 0.75) !important;
                 border-color: rgba(0, 0, 0, 0.08) !important;
            }
             @media (prefers-color-scheme: dark) {
                 .fluent-enabled table[cellpadding='2'][style*='width:100%'] {
                     background-color: rgba(44, 44, 44, 0.7) !important;
                     border-color: rgba(255, 255, 255, 0.1) !important;
                }
            }
             table[style*='background:#f3f9ff'][style*='float:right'][style*='border:1px dashed #aaa'] {
                 background-color: var(--card-background) !important;
                 border: 1px solid var(--card-border) !important;
                 border-radius: 8px;
                 box-shadow: 0 4px 12px var(--card-shadow);
                 margin-left: 1em !important;
                 margin-bottom: 1em !important;
                 padding: 16px !important;
                 float: right;
                 overflow: hidden;
                 color: var(--text-primary);
                 box-sizing: border-box;
            }
             table[style*='background:#f3f9ff'][style*='float:right'][style*='border:1px dashed #aaa'] td {
                 background-color: transparent !important;
                 border: none !important;
                 padding: 0 !important;
                 vertical-align: top;
                 color: var(--text-primary);
            }
             table[style*='background:#f3f9ff'][style*='float:right'][style*='border:1px dashed #aaa'] a {
                 color: var(--link-color);
                 text-decoration: none;
            }
             table[style*='background:#f3f9ff'][style*='float:right'][style*='border:1px dashed #aaa'] a:hover {
                 text-decoration: underline;
            }
             table[style*='background:#f3f9ff'][style*='float:right'][style*='border:1px dashed #aaa'] p, table[style*='background:#f3f9ff'][style*='float:right'][style*='border:1px dashed #aaa'] li {
                 color: var(--text-primary);
                 line-height: 1.5;
            }
             .fluent-enabled table[style*='background:#f3f9ff'][style*='float:right'][style*='border:1px dashed #aaa'] {
                 -webkit-backdrop-filter: blur(20px) saturate(1.8);
                 backdrop-filter: blur(20px) saturate(1.8);
                 background-color: rgba(245, 245, 245, 0.75) !important;
                 border-color: rgba(0, 0, 0, 0.08) !important;
            }
             @media (prefers-color-scheme: dark) {
                 .fluent-enabled table[style*='background:#f3f9ff'][style*='float:right'][style*='border:1px dashed #aaa'] {
                     background-color: rgba(44, 44, 44, 0.7) !important;
                     border-color: rgba(255, 255, 255, 0.1) !important;
                }
            }
             .bar-chart {
                 border-collapse: separate;
                 border-spacing: 0;
                 width: auto;
                 float: right;
                 margin-left: 1em;
            }
             .bar-chart th, .bar-chart td {
                 padding: 8px 12px;
                 border: none;
                 vertical-align: middle;
                 color: var(--text-primary);
                 background-color: transparent !important;
            }
             .bar-chart caption {
                 font-weight: bold;
                 font-size: 1.1em;
                 padding-bottom: 8px;
                 color: var(--text-primary);
            }
             .bar-chart-label-type {
                 font-weight: 600;
                 color: var(--text-secondary);
                 border-bottom: 1px solid var(--divider-color);
            }
             .bar-chart-bar-line {
                 height: 1.2em;
                 border-radius: 2px;
                 background-color: #0078D7;
                 box-shadow: inset 0 0 3px rgba(0, 0, 0, 0.1);
                 transition: background-color 0.3s ease-in-out;
            }
             @media (prefers-color-scheme: dark) {
                 .bar-chart-bar-line {
                     background-color: #85B9F3;
                }
            }
             .bar-chart-bar-numbers {
                 padding-left: 8px;
                 font-weight: 600;
                 color: var(--text-primary);
                 white-space: nowrap;
            }
             .bar-chart .flagicon img {
                 vertical-align: middle;
                 margin-right: 5px;
                 border-radius: 2px;
            }
             .bar-chart a {
                 color: var(--link-color);
                 text-decoration: none;
            }
             .bar-chart a:hover {
                 text-decoration: underline;
            }
             .mw-parser-output .mp-row {
                 display: flex;
                 flex-wrap: wrap;
                 gap: 16px;
                 margin-bottom: 16px;
            }
             .mw-parser-output .mp-row .mp-box {
                 flex: 1 1 45%;
                 min-width: 300px;
                 margin: 0 !important;
            }
             .mw-parser-output .gallery.mw-gallery-traditional {
                 display: grid;
                 grid-template-columns: repeat(auto-fit, minmax(80px, 1fr));
                 gap: 10px;
                 list-style: none;
                 padding: 0;
                 margin: 16px 0 0 0;
            }
             .mw-parser-output .gallerybox {
                 text-align: center;
                 background-color: transparent;
                 border: none;
                 padding: 0;
                 margin: 0;
                 box-sizing: border-box;
                 width: auto !important;
            }
             .mw-parser-output .gallerybox>div {
                 display: flex;
                 flex-direction: column;
                 align-items: center;
                 justify-content: center;
                 height: 100%;
                 width: 100%;
                 padding: 8px;
                 border-radius: 4px;
                 transition: background-color 0.15s ease-in-out;
            }
             .mw-parser-output .gallerybox>div:hover {
                 background-color: var(--card-hover-background);
            }
             .mw-parser-output .gallerybox .thumb {
                 width: auto !important;
                 height: auto;
                 margin: 0 auto;
                 display: flex;
                 align-items: center;
                 justify-content: center;
                 overflow: hidden;
                 padding: 5px;
            }
             .mw-parser-output .gallerybox .thumb div {
                 margin: 0 !important;
                 width: auto !important;
                 height: auto;
            }
             .mw-parser-output .gallerybox img {
                 max-width: 48px;
                 max-height: 48px;
                 width: auto;
                 height: auto;
                 object-fit: contain;
                 border-radius: 4px;
            }
             .mw-parser-output .gallerytext {
                 font-size: 0.85em;
                 margin-top: 8px;
                 color: var(--text-primary);
                 line-height: 1.3;
            }
             .mw-parser-output .gallerytext center {
                 display: block;
            }
             .mw-parser-output .gallerytext a {
                 color: var(--link-color);
                 text-decoration: none;
            }
             .mw-parser-output .gallerytext a:hover {
                 text-decoration: underline;
            }
             .mw-parser-output .mp-box h2 {
                 border-bottom: none;
                 margin-top: 0;
                 padding-bottom: 0;
                 font-size: 1.3em;
                 color: var(--text-primary);
            }
             .mw-parser-output .mp-box h2 .mw-headline a {
                 color: inherit;
                 text-decoration: none;
            }
             .mw-parser-output .mp-box h2 .mw-headline a:hover {
                 text-decoration: underline;
            }
             .navbox-subgroup, .navbox-subgroup .navbox-inner, .navbox-subgroup .navbox-title, .navbox-subgroup .navbox-group, .navbox-subgroup .navbox-list {
                 background-color: transparent !important;
                 border: none !important;
                 box-shadow: none !important;
                 margin: 0 !important;
                 padding: 0 !important;
            }
             .navbox-subgroup .navbox-group {
                 background-color: var(--card-header-background) !important;
                 border-top: 1px solid var(--divider-color) !important;
                 font-size: 0.75em !important;
                 font-weight: 600;
                 text-transform: uppercase;
                 padding: 8px 12px !important;
                 text-align: left !important;
                 border-radius: 0;
            }
             .navbox-subgroup .navbox-list ul, .navbox-subgroup .navbox-list li {
                 margin: 0;
                 padding: 0;
                 list-style: none;
                 color: var(--text-primary);
            }
             .navbox-subgroup .navbox-list li {
                 display: inline;
                 padding: 0 0.5em;
            }
             .navbox-subgroup .navbox-list li:not(:last-child)::after {
                 content: ' · ';
                 font-weight: bold;
                 margin-right: 0.5em;
            }
             .navbox-inner>.navbox-image {
                 float: right;
                 margin: 10px 0 10px 15px;
            }
             .navbox-inner>.navbox-image img {
                 border-radius: 4px;
                 max-width: 60px;
                 height: auto;
            }
             .navbox-subgroup .navbox-list a {
                 color: var(--link-color);
                 text-decoration: none;
            }
             .navbox-subgroup .navbox-list a:hover {
                 text-decoration: underline;
            }
             .mw-collapsible-content {
                 background-color: transparent !important;
            }
             .mw-parser-output .navbox-list {
                 background-color: transparent !important;
            }
             .mw-parser-output .navbox-even, .mw-parser-output .navbox-odd {
                 background-color: transparent !important;
            }
             .ombox {
                 background-color: var(--card-background) !important;
                 border: 1px solid var(--card-border) !important;
                 padding: 16px !important;
                 border-radius: 8px !important;
                 box-shadow: 0 4px 12px var(--card-shadow) !important;
                 margin-bottom: 16px !important;
                 overflow: hidden !important;
                 box-sizing: border-box;
                 border-left-width: 8px;
                 border-left-style: solid;
            }
             .ombox .mbox-text, .ombox p, .ombox li, .ombox a {
                 color: var(--text-primary);
            }
             .ombox a {
                 color: var(--link-color);
                 text-decoration: none;
            }
             .ombox a:hover {
                 text-decoration: underline;
            }
             .ombox.mbox-small {
                 padding: 12px !important;
                 font-size: 0.9em;
            }
             .build-list-item {
                 display: flex;
                 align-items: center;
                 margin-bottom: 0.5em;
                 color: var(--text-primary);
            }
             .build-list-item img {
                 margin-right: 8px;
                 vertical-align: middle;
                 width: 16px;
                 height: 16px;
                 border-radius: 2px;
            }
             .bl-available, .bl-leaked {
                 color: #34c759;
            }
             .bl-confirmed {
                 color: #007aff;
            }
             .bl-unconfirmed {
                 color: #ff9500;
            }
             .bl-fake {
                 color: #ff3b30;
            }
             @media (prefers-color-scheme: dark) {
                 .bl-available, .bl-leaked {
                     color: #30d158;
                }
                 .bl-confirmed {
                     color: #0a84ff;
                }
                 .bl-unconfirmed {
                     color: #ff9f0a;
                }
                 .bl-fake {
                     color: #ff453a;
                }
            }
             .bl-label {
                 background-color: var(--card-hover-background);
                 border: 1px solid var(--divider-color);
                 border-radius: 1em;
                 color: var(--text-primary);
                 font-size: 90%;
                 font-weight: normal;
                 margin-left: .5em;
                 padding: .25em .5em;
                 white-space: nowrap;
            }
             .ombox hr {
                 border-top: 1px solid var(--divider-color);
                 margin: 1em 0;
            }
            ";
        }

        private static async Task<StorageFile> GetThemeFileAsync()
        {
            return await ApplicationData.Current.LocalFolder.CreateFileAsync(
                ThemeCssFileName,
                CreationCollisionOption.OpenIfExists
            );
        }

        public static async Task<string> GetThemeCssAsync()
        {
            var file = await GetThemeFileAsync();
            string content = await FileIO.ReadTextAsync(file);

            if (string.IsNullOrEmpty(content))
            {
                string defaultCss = GetDefaultThemeCss();
                await FileIO.WriteTextAsync(file, defaultCss);
                return defaultCss;
            }

            return content;
        }

        public static async Task ResetThemeToDefaultAsync()
        {
            var file = await GetThemeFileAsync();
            await FileIO.WriteTextAsync(file, GetDefaultThemeCss());
        }
    }
}
