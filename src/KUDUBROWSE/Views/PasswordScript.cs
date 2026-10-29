namespace Kudubrowse.Views
{
    /// <summary>
    /// The small script added to every top-level page for passwords. It tells the browser when a login box gets focus
    /// (so the browser can show its own list of saved logins next to it) and what was typed when a login form is sent
    /// (so the browser can offer to save it). It only fills fields when the browser sends a login, which happens only
    /// after the person picks one in the browser's list; the page never gets saved logins otherwise.
    /// </summary>
    public static class PasswordScript
    {
        public const string Source = @"(() => {
  if (window.top !== window || window.__kudubrowsePasswords) return;
  window.__kudubrowsePasswords = true;
  const post = m => { try { window.chrome.webview.postMessage(m); } catch (e) {} };
  const visible = el => el.offsetWidth > 0 && el.offsetHeight > 0;
  const isPw = el => el && el.tagName === 'INPUT' && el.type === 'password';
  const isUser = el => el && el.tagName === 'INPUT' && ['text', 'email', 'tel', ''].includes(el.type);
  const scope = el => (el && (el.form || (el.closest && el.closest('form')))) || document;
  const pwFields = root => Array.from(root.querySelectorAll('input[type=password]')).filter(visible);
  const isNew = pw => /new-password/.test(pw.autocomplete || '') || pwFields(scope(pw)).length >= 2;
  const userFor = pw => {
    const inputs = Array.from(scope(pw).querySelectorAll('input')).filter(visible);
    const named = inputs.find(i => /username|email/.test(i.autocomplete || ''));
    if (named) return named;
    for (let i = inputs.indexOf(pw) - 1; i >= 0; i--) if (isUser(inputs[i])) return inputs[i];
    return null;
  };
  let field = null, shown = false;
  document.addEventListener('focusin', e => {
    const el = e.target;
    let pw = isPw(el) ? el : null;
    if (!pw && isUser(el)) pw = pwFields(scope(el))[0] || null;
    if (!pw) return;
    field = el;
    shown = true;
    const r = el.getBoundingClientRect();
    post({ kudu: 'field', x: r.left, y: r.bottom, w: r.width, isNew: isNew(pw), isPassword: isPw(el) });
  }, true);
  const hide = () => { if (shown) { shown = false; post({ kudu: 'blur' }); } };
  document.addEventListener('focusout', e => { if (e.target === field) setTimeout(hide, 150); }, true);
  window.addEventListener('scroll', hide, true);
  let last = '';
  const capture = root => {
    const filled = pwFields(root).filter(p => p.value);
    if (!filled.length) return;
    const pw = filled.find(p => /new-password/.test(p.autocomplete || '')) || filled[filled.length - 1];
    const user = userFor(filled[0]);
    const msg = { kudu: 'login', username: user ? user.value : '', password: pw.value };
    const key = msg.username + '\n' + msg.password;
    if (key === last) return;
    last = key;
    post(msg);
  };
  document.addEventListener('submit', e => capture(scope(e.target)), true);
  document.addEventListener('click', e => {
    const b = e.target && e.target.closest && e.target.closest('button, input[type=submit], input[type=button], [role=button]');
    if (b) capture(scope(b));
  }, true);
  document.addEventListener('keydown', e => { if (e.key === 'Enter' && (isPw(e.target) || isUser(e.target))) capture(scope(e.target)); }, true);
  const set = (el, v) => {
    if (!el) return;
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(el, v);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  };
  window.chrome.webview.addEventListener('message', e => {
    const d = e.data;
    if (!d || d.kudu !== 'fill' || !field) return;
    if (d.generated) {
      const all = pwFields(scope(field));
      (all.length >= 3 ? all.slice(1) : all).forEach(p => set(p, d.password));
      return;
    }
    const pw = isPw(field) ? field : pwFields(scope(field))[0];
    set(userFor(pw), d.username);
    set(pw, d.password);
  });
})();";
    }
}
