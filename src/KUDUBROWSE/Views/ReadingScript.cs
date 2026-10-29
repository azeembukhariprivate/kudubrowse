using System.Globalization;

namespace Kudubrowse.Views
{
    /// <summary>
    /// Reading mode: Mozilla's Readability (in Assets) finds the page's article, and this shows it as clean text over the
    /// page, with buttons for the text size and the colours. Running it again (or Esc, or the close button) closes it.
    /// The script returns "open", "closed" or "none" (no article found).
    /// </summary>
    public static class ReadingScript
    {
        public static string Build(string readability, int size, string theme) =>
            "(() => {\n" + Script
                .Replace("/*READABILITY*/", readability)
                .Replace("/*SIZE*/", size.ToString(CultureInfo.InvariantCulture))
                .Replace("/*THEME*/", theme == "sepia" || theme == "dark" ? theme : "light") + "\n})()";

        private const string Script = @"
  const old = document.getElementById('kudubrowse-reader');
  if (old) { old.__close(); return 'closed'; }
  /*READABILITY*/
  let article = null;
  try { article = new Readability(document.cloneNode(true), { charThreshold: 300 }).parse(); } catch (e) {}
  if (!article || !article.content || (article.textContent || '').trim().length < 200) return 'none';

  let size = /*SIZE*/, theme = '/*THEME*/';
  const host = document.createElement('div');
  host.id = 'kudubrowse-reader';
  host.style.cssText = 'all:initial;position:fixed;inset:0;z-index:2147483647;';
  const root = host.attachShadow({ mode: 'closed' });
  root.innerHTML = `<style>
    :host { all: initial; }
    .kb-page { position: fixed; inset: 0; overflow-y: auto; outline: none; font-family: Georgia, 'Times New Roman', serif; }
    .kb-page.light { background: #ffffff; color: #1f2328; }
    .kb-page.sepia { background: #f4ecd8; color: #4b3a2a; }
    .kb-page.dark { background: #1e1f22; color: #e3e3e3; }
    .kb-bar { position: sticky; top: 0; display: flex; gap: 6px; justify-content: flex-end; padding: 10px 16px;
           font-family: 'Segoe UI', sans-serif; font-size: 13px; background: inherit; border-bottom: 1px solid rgba(128,128,128,.25); }
    .kb-bar button { font: inherit; padding: 5px 12px; border-radius: 6px; border: 1px solid rgba(128,128,128,.4);
                  background: transparent; color: inherit; cursor: pointer; }
    .kb-bar button:hover { background: rgba(128,128,128,.15); }
    .kb-bar button.on { border-color: #0E8C7E; box-shadow: inset 0 0 0 1px #0E8C7E; }
    .kb-article { max-width: 720px; margin: 0 auto; padding: 28px 24px 80px; line-height: 1.65; }
    .kb-article > h1 { font-size: 1.7em; line-height: 1.25; margin: 0 0 6px; }
    .kb-by { font-family: 'Segoe UI', sans-serif; font-size: .75em; opacity: .7; margin-bottom: 26px; }
    img, video, figure, iframe { max-width: 100%; height: auto; }
    a { color: #0E8C7E; }
    .dark a { color: #4fd1c0; }
    pre { overflow-x: auto; padding: 12px; background: rgba(128,128,128,.12); border-radius: 6px; font-size: .8em; }
    blockquote { margin: 0; padding-left: 16px; border-left: 3px solid rgba(128,128,128,.4); }
  </style>
  <div class='kb-page'>
    <div class='kb-bar'>
      <button data-a='smaller' title='Smaller text'>A−</button>
      <button data-a='bigger' title='Bigger text'>A+</button>
      <button data-t='light'>Light</button>
      <button data-t='sepia'>Sepia</button>
      <button data-t='dark'>Dark</button>
      <button data-a='close' title='Close reading mode (Esc)'>Close</button>
    </div>
    <article class='kb-article'><h1></h1><div class='kb-by'></div><div class='kb-text'></div></article>
  </div>`;
  const page = root.querySelector('.kb-page');
  root.querySelector('.kb-article > h1').textContent = article.title || document.title;
  root.querySelector('.kb-by').textContent = [article.byline, article.siteName].filter(Boolean).join(' · ');
  // The article comes from the page itself; anything that could run is left out.
  const tpl = document.createElement('template');
  tpl.innerHTML = article.content;
  tpl.content.querySelectorAll('script, style, link, object, embed, form').forEach(n => n.remove());
  tpl.content.querySelectorAll('*').forEach(n => {
    for (const a of Array.from(n.attributes)) if (/^on/i.test(a.name) || /^\s*javascript:/i.test(a.value)) n.removeAttribute(a.name);
  });
  root.querySelector('.kb-text').appendChild(tpl.content);

  const show = () => {
    page.className = 'kb-page ' + theme;
    root.querySelector('.kb-article').style.fontSize = size + 'px';
    root.querySelectorAll('[data-t]').forEach(b => b.classList.toggle('on', b.dataset.t === theme));
  };
  const keep = () => { try { window.chrome.webview.postMessage({ kudu: 'reading', size, theme }); } catch (e) {} };
  const overflow = document.documentElement.style.overflow;
  const onKey = e => { if (e.key === 'Escape') { e.stopPropagation(); close(); } };
  const close = () => {
    host.remove();
    document.documentElement.style.overflow = overflow;
    window.removeEventListener('keydown', onKey, true);
  };
  host.__close = close;
  root.querySelector('.kb-bar').addEventListener('click', e => {
    const b = e.target.closest('button');
    if (!b) return;
    if (b.dataset.t) theme = b.dataset.t;
    else if (b.dataset.a === 'smaller') size = Math.max(14, size - 2);
    else if (b.dataset.a === 'bigger') size = Math.min(32, size + 2);
    else if (b.dataset.a === 'close') { close(); return; }
    show();
    keep();
  });
  window.addEventListener('keydown', onKey, true);
  show();
  document.documentElement.style.overflow = 'hidden';
  document.documentElement.appendChild(host);
  page.tabIndex = -1;
  page.focus();
  return 'open';";
    }
}
