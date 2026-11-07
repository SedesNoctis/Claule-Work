"""Builds the author's rules table (xlsx) from Таблица-правил.tsv. Run: python make_table.py <tsv> <xlsx>."""
import sys, csv
from openpyxl import Workbook
from openpyxl.styles import Alignment, Font, PatternFill, Border, Side
src, out = sys.argv[1], sys.argv[2]
rows = list(csv.reader(open(src, encoding='utf-8'), delimiter='\t'))
wb = Workbook(); ws = wb.active; ws.title = "Правила"
widths = [8, 14, 34, 70, 14, 22, 26, 44]
fills = {"Сделано": "DCEFD8", "Записано": "FFF3CC", "Не сделано": "F8D7D3"}
thin = Side(style='thin', color='BBBBBB')
for r, row in enumerate(rows, 1):
    for c, v in enumerate(row, 1):
        cell = ws.cell(row=r, column=c, value=v)
        cell.alignment = Alignment(wrap_text=True, vertical='top')
        cell.border = Border(top=thin, bottom=thin, left=thin, right=thin)
        if r == 1:
            cell.font = Font(bold=True, color='FFFFFF'); cell.fill = PatternFill('solid', fgColor='5B4636')
        elif c == 6:
            key = next((k for k in fills if v.startswith(k)), None)
            if key: cell.fill = PatternFill('solid', fgColor=fills[key])
for i, w in enumerate(widths, 1): ws.column_dimensions[chr(64 + i)].width = w
ws.freeze_panes = "C2"; ws.auto_filter.ref = ws.dimensions
wb.save(out)
