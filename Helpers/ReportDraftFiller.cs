using System.Text.Json;
using Amaranth10API.Models;
using Microsoft.Web.WebView2.Core;

namespace Amaranth10API.Helpers;

internal static class ReportDraftFiller
{
    public static string CreateScript(ReportDraftFill fill)
    {
        string payload = JsonSerializer.Serialize(new
        {
            startYear = fill.StartDate.Year,
            startMonth = fill.StartDate.Month,
            startDay = fill.StartDate.Day,
            endYear = fill.EndDate.Year,
            endMonth = fill.EndDate.Month,
            endDay = fill.EndDate.Day,
            dayCount = fill.DayCount,
            startTime = fill.StartTime ?? string.Empty,
            endTime = fill.EndTime ?? string.Empty,
            kind = fill.Kind ?? string.Empty,
            overnight = fill.DayCount > 1,
            holidayDays = fill.HolidayDays.Select(day => new
            {
                year = day.Year,
                month = day.Month,
                day = day.Day,
                weekday = day.Weekday,
                startTime = day.StartTime,
                endTime = day.EndTime
            }).ToArray()
        });

        return Script.Replace("__PAYLOAD__", payload);
    }

    public static async Task TryFillAsync(CoreWebView2 core, ReportDraftFill fill)
    {
        await core.ExecuteScriptAsync(CreateScript(fill));
    }

    private const string Script = """
(function () {
  if (window.__amattahFillStarted) return;
  window.__amattahFillStarted = true;
  const data = __PAYLOAD__;
  const deadline = Date.now() + 25000;
  let tries = 0;

  function docsFrom(root) {
    const list = [root];
    const frames = root.querySelectorAll('iframe');
    for (let i = 0; i < frames.length; i++) {
      try {
        const child = frames[i].contentDocument;
        if (child) list.push.apply(list, docsFrom(child));
      } catch (e) {}
    }
    return list;
  }

  function allDocs() {
    return docsFrom(document);
  }

  function norm(text) {
    return String(text || '').replace(/\s+/g, '');
  }

  function fire(el) {
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
    try {
      el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText' }));
    } catch (e) {}
  }

  function setSelect(select, wanted) {
    const raw = String(wanted);
    const compact = raw.replace(/^0+/, '') || '0';
    for (let i = 0; i < select.options.length; i++) {
      const opt = select.options[i];
      const texts = [opt.value, opt.text, opt.label].map(function (v) { return String(v || '').trim(); });
      for (let t = 0; t < texts.length; t++) {
        const value = texts[t];
        if (!value) continue;
        if (value === raw || value === compact || value.replace(/^0+/, '') === compact) {
          select.selectedIndex = i;
          fire(select);
          return true;
        }
      }
    }
    return false;
  }

  function findRow(doc, label) {
    const nodes = doc.querySelectorAll('td, th, p, div, span, label');
    for (let i = 0; i < nodes.length; i++) {
      if (norm(nodes[i].textContent).indexOf(norm(label)) === -1) continue;
      return nodes[i].closest('tr') || nodes[i].parentElement;
    }
    return null;
  }

  function directCells(row) {
    const cells = [];
    if (!row) return cells;
    for (let i = 0; i < row.children.length; i++) {
      const tag = row.children[i].tagName;
      if (tag === 'TD' || tag === 'TH') cells.push(row.children[i]);
    }
    return cells;
  }

  function leafCells(row) {
    const all = row.querySelectorAll('td, th');
    const leaves = [];
    for (let i = 0; i < all.length; i++) {
      if (!all[i].querySelector('td, th')) leaves.push(all[i]);
    }
    return leaves.length ? leaves : directCells(row);
  }

  function writeHtml(target, html) {
    if (!target) return;
    const editable = target.querySelector ? target.querySelector('[contenteditable="true"]') : null;
    const p = target.querySelector ? target.querySelector('p') : null;
    const dest = editable || p || target;
    dest.innerHTML = html;
    fire(dest);
    if (dest !== target) fire(target);
  }

  function suffixHtml(suffix) {
    return suffix ? '&nbsp;<span style="font-size:9pt;">' + suffix + '</span>' : '';
  }

  function dateHtml(y, m, d, suffix) {
    return '&nbsp;' + y + ' 년&nbsp;' + m + ' 월&nbsp;' + d + ' 일' + suffixHtml(suffix);
  }

  function looksLikeDate(text) {
    const value = String(text || '');
    return value.indexOf('년') !== -1 && value.indexOf('월') !== -1 && value.indexOf('일') !== -1;
  }

  function isPeriodDateCell(cell) {
    const text = cell && cell.textContent;
    const compact = norm(text);
    if (!looksLikeDate(text)) return false;
    if (compact.indexOf('출장일수') !== -1) return false;
    if (compact === '출장기간') return false;
    return true;
  }

  function writeKoreanDate(cell, y, m, d, suffix) {
    writeHtml(cell, dateHtml(y, m, d, suffix));
  }

  function writePeriodRange(cell, start, end) {
    const startHtml = dateHtml(start.y, start.m, start.d, '부터');
    const endHtml = dateHtml(end.y, end.m, end.d, '까지');
    const paragraphs = cell.querySelectorAll('p');
    if (paragraphs.length >= 2 && looksLikeDate(paragraphs[0].textContent) && looksLikeDate(paragraphs[1].textContent)) {
      paragraphs[0].innerHTML = startHtml;
      paragraphs[1].innerHTML = endHtml;
      fire(paragraphs[0]);
      fire(paragraphs[1]);
      fire(cell);
      return;
    }
    writeHtml(cell, startHtml + '&nbsp;' + endHtml);
  }

  function durationText(startTime, endTime) {
    if (!startTime || !endTime) return '';
    function toMin(value) {
      const parts = String(value).split(':');
      return (parseInt(parts[0], 10) || 0) * 60 + (parseInt(parts[1], 10) || 0);
    }
    let mins = toMin(endTime) - toMin(startTime);
    if (mins <= 0) mins += 24 * 60;
    const hours = mins / 60;
    if (hours >= 8) return '1';
    if (hours >= 4) return '0.5';
    return '';
  }

  function fillDayCount(docs, count) {
    for (let d = 0; d < docs.length; d++) {
      const row = findRow(docs[d], '출장일수');
      if (!row) continue;
      const inputs = row.querySelectorAll('input, textarea');
      if (inputs.length) {
        inputs[0].value = String(count);
        fire(inputs[0]);
        continue;
      }
      const cells = leafCells(row);
      if (cells.length >= 2) {
        const cell = cells[cells.length - 1];
        if (!cell.querySelector('select') && norm(cell.textContent).indexOf('출장일수') === -1) {
          writeHtml(cell, '(&nbsp;' + count + '&nbsp;) 일');
        }
      }
    }
  }

  function fillChoice(docs, label, wanted) {
    if (!wanted) return;
    for (let d = 0; d < docs.length; d++) {
      const row = findRow(docs[d], label);
      if (!row) continue;
      const selects = row.querySelectorAll('select');
      for (let i = 0; i < selects.length; i++) {
        const options = selects[i].options;
        for (let o = 0; o < options.length; o++) {
          if (norm(options[o].text).indexOf(norm(wanted)) !== -1) {
            selects[i].selectedIndex = o;
            fire(selects[i]);
          }
        }
      }
    }
  }

  function fillTrip(docs) {
    if (String(data.kind || '').indexOf('휴일') !== -1) return false;
    const start = { y: data.startYear, m: data.startMonth, d: data.startDay };
    const end = { y: data.endYear, m: data.endMonth, d: data.endDay };
    let found = false;
    for (let d = 0; d < docs.length; d++) {
      const row = findRow(docs[d], '출장기간');
      if (!row) continue;
      found = true;
      const cells = leafCells(row);
      const dateCells = [];
      for (let i = 0; i < cells.length; i++) {
        if (isPeriodDateCell(cells[i])) dateCells.push(cells[i]);
      }
      if (dateCells.length >= 2) {
        writeKoreanDate(dateCells[0], start.y, start.m, start.d, '부터');
        writeKoreanDate(dateCells[1], end.y, end.m, end.d, '까지');
      } else if (dateCells.length === 1) {
        writePeriodRange(dateCells[0], start, end);
      } else {
        const own = directCells(row);
        let labelIndex = -1;
        for (let i = 0; i < own.length; i++) {
          if (norm(own[i].textContent).indexOf('출장기간') !== -1) {
            labelIndex = i;
            break;
          }
        }
        if (labelIndex >= 0 && own[labelIndex + 1] && norm(own[labelIndex + 1].textContent).indexOf('출장일수') === -1) {
          writePeriodRange(own[labelIndex + 1], start, end);
        }
      }
    }
    fillDayCount(docs, data.dayCount);
    fillChoice(docs, '구분', data.overnight ? '숙박' : '당일');
    fillChoice(docs, '출장구분', '프로젝트');
    return found;
  }

  function isHolidayDateCell(cell) {
    const text = cell && cell.textContent;
    const compact = norm(text);
    if (!looksLikeDate(text)) return false;
    if (compact.indexOf('출장') !== -1 || compact.indexOf('날짜') !== -1) return false;
    if (compact.indexOf('합계') !== -1) return false;
    if (cell.querySelector && cell.querySelector('td, th, select')) return false;
    return compact.length < 24;
  }

  function fillHolidayDate(cell, day) {
    const paragraphs = Array.prototype.slice.call(cell.querySelectorAll('p'));
    let dateP = null;
    let weekP = null;
    for (let i = 0; i < paragraphs.length; i++) {
      const text = paragraphs[i].textContent || '';
      if (looksLikeDate(text)) dateP = paragraphs[i];
      else if (text.indexOf('요일') !== -1) weekP = paragraphs[i];
    }
    if (!dateP && paragraphs[0]) dateP = paragraphs[0];
    if (dateP) {
      dateP.innerHTML = dateHtml(day.year, day.month, day.day, '');
      fire(dateP);
    } else {
      writeHtml(cell, dateHtml(day.year, day.month, day.day, '') + '<br>(' + day.weekday + '요일)');
      return;
    }
    if (weekP) {
      weekP.innerHTML = '(' + day.weekday + '요일)';
      fire(weekP);
    }
    fire(cell);
  }

  function fillHolidayTime(cells, day) {
    const startTime = day.startTime || '';
    const endTime = day.endTime || '';
    if (!startTime || !endTime) return;
    const dur = durationText(startTime, endTime);
    for (let c = 0; c < cells.length; c++) {
      if (isHolidayDateCell(cells[c])) continue;
      const text = cells[c].textContent || '';
      const hasTilde = text.indexOf('~') !== -1 || text.indexOf('～') !== -1;
      const hasDuration = /\(.*일/.test(text);
      if (hasTilde && hasDuration) {
        const paragraphs = cells[c].querySelectorAll('p');
        if (paragraphs.length >= 2) {
          paragraphs[0].innerHTML = startTime + ' ~ ' + endTime;
          fire(paragraphs[0]);
          if (dur) {
            paragraphs[1].innerHTML = '(&nbsp;' + dur + '&nbsp;)일';
            fire(paragraphs[1]);
          }
        } else {
          writeHtml(cells[c], startTime + ' ~ ' + endTime);
        }
      } else if (hasTilde) {
        writeHtml(cells[c], startTime + ' ~ ' + endTime);
      } else if (hasDuration && text.indexOf('합') === -1) {
        if (dur) writeHtml(cells[c], '(&nbsp;' + dur + '&nbsp;)일');
      }
    }
  }

  function fillHoliday(docs) {
    const days = data.holidayDays || [];
    if (String(data.kind || '').indexOf('휴일') === -1 || !days.length) return;
    for (let d = 0; d < docs.length; d++) {
      const labelRow = findRow(docs[d], '휴일근무');
      if (!labelRow) continue;
      const table = labelRow.closest('table') || labelRow;
      const rows = table.querySelectorAll('tr');
      let idx = 0;
      for (let r = 0; r < rows.length && idx < days.length; r++) {
        const rowText = norm(rows[r].textContent);
        if (rowText.indexOf('출장기간') !== -1 || rowText.indexOf('합계') !== -1) continue;
        const cells = leafCells(rows[r]);
        let dateCell = null;
        for (let c = 0; c < cells.length; c++) {
          if (isHolidayDateCell(cells[c])) {
            dateCell = cells[c];
            break;
          }
        }
        if (!dateCell) continue;
        const day = days[idx++];
        fillHolidayDate(dateCell, day);
        fillHolidayTime(cells, day);
      }
    }
  }

  function isReady(docs) {
    for (let d = 0; d < docs.length; d++) {
      if (findRow(docs[d], '출장기간') || findRow(docs[d], '출장일수') || findRow(docs[d], '휴일근무')) return true;
    }
    return false;
  }

  let editorFilled = false;
  const timer = setInterval(function () {
    tries += 1;
    if (Date.now() > deadline) {
      clearInterval(timer);
      return;
    }
    if (editorFilled || !isReady(allDocs())) return;
    editorFilled = true;
    clearInterval(timer);
    setTimeout(function () {
      fillTrip(allDocs());
      fillHoliday(allDocs());
      setTimeout(function () {
        fillTrip(allDocs());
        fillHoliday(allDocs());
      }, 800);
    }, 700);
  }, 400);
})();
""";
}
