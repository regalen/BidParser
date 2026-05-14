// BidParser wireframes — 5 directions for the upload page
// Sketchy, low-fi, ProductLens-light. Rendered onto a DesignCanvas.

const { useState } = React;

// ---------- Inline icons (lucide-ish, stroke style) ----------
const Icon = {
  cloud: (p) =>
  <svg viewBox="0 0 64 44" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="M14 36 C5 36 2 28 7 22 C7 14 18 12 22 17 C24 8 38 8 41 17 C50 15 56 22 53 30 C58 31 60 36 56 40 L14 40 Z" />
      <path d="M32 24 V36 M26 30 L32 24 L38 30" />
    </svg>,

  upload: (p) =>
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="M12 16V4M6 10l6-6 6 6M4 18v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2" />
    </svg>,

  file: (p) =>
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
      <path d="M14 2v6h6" />
    </svg>,

  check: (p) =>
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="M20 6 9 17l-5-5" />
    </svg>,

  chev: (p) =>
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="m6 9 6 6 6-6" />
    </svg>,

  x: (p) =>
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="M18 6 6 18M6 6l12 12" />
    </svg>,

  spinner: (p) =>
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" {...p}>
      <path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M5.6 18.4l2.1-2.1M16.3 7.7l2.1-2.1" />
    </svg>

};

// ---------- Shared chrome ----------
function Header({ stage = 'BidParser' }) {
  return (
    <div className="wf-header">
      <div className="wf-logo">
        <div className="wf-logo-tile">B</div>
        <div className="wf-wordmark">{stage}</div>
      </div>
      <div className="wf-account">
        <div className="wf-avatar">EC</div>
        <span className="lbl">@E.CARTER</span>
      </div>
    </div>);

}

function Dropzone({ size = 'lg', title = 'Drop files here or click to upload', sub = 'PDF, XLSX, CSV  ·  up to 50MB each', extra, dashed = true, accent = false, fluid = false, height }) {
  const dims = {
    sm: { width: 460, height: 200 },
    md: { width: 620, height: 260 },
    lg: { width: 760, height: 320 },
    xl: { width: 880, height: 360 }
  }[size];
  return (
    <div
      className={`dropzone ${size === 'sm' ? 'compact' : ''}`}
      style={{ ...{
          ...dims,
          ...(fluid ? { width: '100%' } : null),
          ...(height != null ? { height } : null),
          borderStyle: dashed ? 'dashed' : 'solid',
          borderColor: accent ? 'var(--accent)' : 'var(--ink)',
          background: accent ? 'var(--accent-soft)' : 'var(--paper)', backgroundColor: "rgb(255, 255, 255)"
        }, borderStyle: "dashed" }}>
      
      <div className="cloud" style={{ color: accent ? 'var(--accent)' : 'var(--ink)' }}>
        <Icon.cloud />
      </div>
      <div className="title">{title}</div>
      {sub ? <div className="sub">{sub}</div> : null}
      {extra}
    </div>);

}

// ---------- A small "scribble" annotation ----------
function Annotation({ x, y, text, w = 200, arrow }) {
  return (
    <div style={{ position: 'absolute', left: x, top: y, width: w, pointerEvents: 'none' }}>
      <div className="anno" style={{ color: 'var(--accent)' }}>{text}</div>
      {arrow}
    </div>);

}
function ArrowDownRight({ length = 60, angle = 35 }) {
  return (
    <svg width={length + 12} height={length + 12} viewBox={`0 0 ${length + 12} ${length + 12}`} style={{ marginTop: 4 }}>
      <path
        d={`M 6 6 Q ${length / 2} ${length * 0.2}, ${length} ${length}`}
        stroke="var(--accent)" strokeWidth="1.5" fill="none" strokeLinecap="round" />
      
      <path d={`M ${length} ${length} l -8 -2 m 8 2 l -2 -8`} stroke="var(--accent)" strokeWidth="1.5" fill="none" strokeLinecap="round" />
    </svg>);

}

// ============================================================
//  V1 — Google Classic
//   Logo + dropzone centered. Vendor + File type as compact
//   selects directly under the box. The most minimal direction.
// ============================================================
function V1_GoogleClassic() {
  return (
    <div className="wf wf-stage" style={{ width: 1100, height: 780 }}>
      <Header />
      <div className="col center" style={{ paddingTop: 90 }}>
        {/* Wordmark */}
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 6 }}>
          <span className="scrib" style={{ fontSize: 56, lineHeight: 1, fontWeight: 700, color: 'var(--ink)' }}>Bid</span>
          <span className="scrib" style={{ fontSize: 56, lineHeight: 1, fontWeight: 700, color: 'var(--accent)' }}>Parser</span>
        </div>
        <div className="lbl lbl-faint mt-2">PARSE SUPPLIER QUOTES INTO STANDARD EXCEL</div>

        <div className="mt-8" />
        <Dropzone size="lg" />

        {/* selects sit below, tightly grouped */}
        <div className="row gap-3 mt-6" style={{ width: 760 }}>
          <div className="col grow gap-2">
            <span className="lbl">VENDOR</span>
            <div className="sel placeholder">
              <span>Choose vendor…</span>
              <Icon.chev style={{ width: 14, height: 14, color: 'var(--ink-mute)' }} />
            </div>
          </div>
          <div className="col grow gap-2">
            <span className="lbl">FILE TYPE</span>
            <div className="sel placeholder" style={{ opacity: 0.5 }}>
              <span>Pick a vendor first</span>
              <Icon.chev style={{ width: 14, height: 14, color: 'var(--ink-mute)' }} />
            </div>
          </div>
        </div>

        <div className="lbl lbl-faint mt-6">YOUR PARSED FILES STAY PRIVATE  ·  AUTO-DELETED AFTER 24H</div>
      </div>

      <Annotation x={780} y={420} text="cascading select — file types swap on vendor change" w={240} arrow={<ArrowDownRight length={80} />} />
    </div>);

}

window.V1_GoogleClassic = V1_GoogleClassic;
window.Header = Header;
window.Dropzone = Dropzone;
window.Annotation = Annotation;
window.ArrowDownRight = ArrowDownRight;
window.Icon = Icon;