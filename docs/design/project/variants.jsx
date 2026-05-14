// V2–V6 wireframe variants. Uses globals from wireframes.jsx.

const { Header: WHeader, Dropzone: WDropzone, Annotation: WAnno, ArrowDownRight: WArrow, Icon: WIcon } = window;

// ============================================================
//  V2 — Pill Filters Above
//   Wordmark + two rows of segmented pills (Vendor → File Type),
//   then a generous dropzone below. Filters feel like search facets.
// ============================================================
function V2_PillFilters() {
  const vendors = ['Nutanix', 'Dell', 'Lenovo'];
  const types = ['Renewal (PDF)', 'Hardware Only (PDF)', 'Software Only (PDF)'];
  return (
    <div className="wf wf-stage" style={{ width: 1100, height: 780 }}>
      <WHeader />
      <div className="col center" style={{ paddingTop: 56 }}>
        <div className="scrib" style={{ fontSize: 44, lineHeight: 1, fontWeight: 700 }}>
          Bid<span style={{ color: 'var(--accent)' }}>Parser</span>
        </div>
        <div className="hand" style={{ fontSize: 16, color: 'var(--ink-soft)', marginTop: 6 }}>
          Turn vendor quotes into a clean Excel import.
        </div>

        {/* Vendor pill row */}
        <div className="col gap-2 mt-8" style={{ width: 820 }}>
          <span className="lbl">1 · VENDOR</span>
          <div className="row gap-2">
            {vendors.map((v, i) =>
            <div key={v} className={`chip ${i === 0 ? 'active' : ''}`}>{v}</div>
            )}
            <div className="chip ghost">+ Add vendor</div>
          </div>
        </div>

        {/* File type pill row */}
        <div className="col gap-2 mt-4" style={{ width: 820 }}>
          <span className="lbl">2 · FILE TYPE  <span style={{ fontWeight: 400, color: 'var(--ink-mute)' }}>· for Nutanix</span></span>
          <div className="row gap-2">
            {types.map((t, i) =>
            <div key={t} className={`chip ${i === 1 ? 'active' : ''}`}>{t}</div>
            )}
          </div>
        </div>

        <div className="mt-6" />
        <WDropzone size="lg" />

        <div className="lbl lbl-faint mt-4">DROP MULTIPLE FILES TO QUEUE A BATCH</div>
      </div>

      <WAnno x={860} y={170} w={220}
      text="vendor first; types swap inline below"
      arrow={<WArrow length={70} />} />
      
    </div>);

}

// ============================================================
//  V3 — Wizard Strip (locked dropzone)
//   Three horizontal cards: Vendor → File Type → Drop.
//   Steps 1 & 2 are inline; the dropzone is dimmed until both are set.
// ============================================================
function V3_WizardStrip() {
  return (
    <div className="wf wf-stage" style={{ width: 1100, height: 780 }}>
      <WHeader />
      <div className="col center" style={{ paddingTop: 60 }}>
        <div className="scrib" style={{ fontSize: 36, lineHeight: 1, fontWeight: 700 }}>
          Let's parse a quote.
        </div>
        <div className="hand" style={{ fontSize: 15, color: 'var(--ink-soft)', marginTop: 4 }}>
          Three quick steps.
        </div>

        {/* Step strip */}
        <div className="row gap-4 mt-8" style={{ width: 980 }}>
          {/* Step 1 — done */}
          <StepCard num="1" title="VENDOR" state="done">
            <div className="hand" style={{ fontSize: 17 }}>Nutanix</div>
          </StepCard>
          {/* Step 2 — active */}
          <StepCard num="2" title="FILE TYPE" state="active">
            <div className="col gap-2">
              <div className="chip sm active">Renewal (PDF)</div>
              <div className="chip sm">Hardware Only</div>
              <div className="chip sm">Software Only</div>
            </div>
          </StepCard>
          {/* Step 3 — locked */}
          <StepCard num="3" title="UPLOAD" state="locked">
            <div className="hand" style={{ fontSize: 13, color: 'var(--ink-mute)' }}>
              Pick a file type to unlock
            </div>
          </StepCard>
        </div>

        <div className="mt-8" />
        <div style={{ opacity: 0.4 }}>
          <WDropzone size="lg" />
        </div>
      </div>

      <WAnno x={780} y={310} w={220}
      text="each step expands inline — no page changes"
      arrow={<WArrow length={70} />} />
      
    </div>);

}

function StepCard({ num, title, state, children }) {
  const isActive = state === 'active';
  const isDone = state === 'done';
  const isLocked = state === 'locked';
  return (
    <div
      className="stroke"
      style={{
        flex: 1,
        minHeight: 180,
        padding: 18,
        borderRadius: 12,
        borderColor: isActive ? 'var(--accent)' : isLocked ? 'var(--ink-faint)' : 'var(--ink)',
        background: isActive ? 'var(--accent-soft)' : 'var(--paper)',
        opacity: isLocked ? 0.55 : 1,
        position: 'relative'
      }}>
      
      <div className="row between" style={{ marginBottom: 12 }}>
        <span className="lbl" style={{ color: isActive ? 'var(--accent)' : 'var(--ink-soft)' }}>STEP {num} · {title}</span>
        {isDone &&
        <div style={{ color: 'var(--accent)', width: 16, height: 16 }}>
            <WIcon.check style={{ width: 16, height: 16 }} />
          </div>
        }
      </div>
      {children}
    </div>);

}

// ============================================================
//  V4 — Side Panel Form
//   Left card: vendor + file type as labelled selects, plus
//   tiny "what is this?" affordances. Right: huge dropzone.
//   Most utilitarian. Reads as an internal tool.
// ============================================================
function V4_SidePanel() {
  return (
    <div className="wf wf-stage" style={{ width: 1280, height: 900 }}>
      <WHeader />
      <div className="col" style={{ padding: '32px 48px' }}>
        <div className="row between" style={{ alignItems: 'flex-end' }}>
          <div>
            <div className="hand" style={{ fontSize: 26, lineHeight: 1.1, color: 'var(--ink)', letterSpacing: '-0.02em' }}>New quote</div>
            <div className="lbl lbl-faint" style={{ marginTop: 6 }}>UPLOAD A VENDOR QUOTE / BID TO PARSE</div>
          </div>
          <button
            type="button"
            className="btn"
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: 6,
              color: '#dc2626',
              borderColor: '#fecaca',
              background: '#fef2f2'
            }}>
            
            <svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M3 12a9 9 0 1 0 3-6.7" />
              <path d="M3 4v5h5" />
            </svg>
            RESET
          </button>
        </div>

        <div className="row gap-6 mt-8" style={{ alignItems: 'stretch' }}>
          {/* Left form card */}
          <div className="stroke col gap-4" style={{ width: 320, padding: 24, borderRadius: 12, background: 'var(--paper)' }}>
            <span className="lbl">PARSE SETTINGS</span>

            <div className="col gap-2">
              <span className="lbl">VENDOR</span>
              <div className="sel">
                <span>Nutanix</span>
                <WIcon.chev style={{ width: 14, height: 14, color: 'var(--ink-mute)' }} />
              </div>
            </div>

            <div className="col gap-2">
              <span className="lbl">FILE TYPE</span>
              <div className="sel">
                <span>Renewal (PDF)</span>
                <WIcon.chev style={{ width: 14, height: 14, color: 'var(--ink-mute)' }} />
              </div>
              <span className="lbl lbl-faint" style={{ textTransform: 'none', letterSpacing: 0, fontWeight: 400, fontSize: 11, fontFamily: 'var(--hand)' }}>
                Types depend on the vendor.
              </span>
            </div>

            {/* Conditional fields — surfaced because vendor = Nutanix */}
            <div className="stroke-faint" style={{ height: 1, marginTop: 4, borderStyle: 'dashed' }} />
            <div className="row between" style={{ alignItems: 'baseline' }}>
              <span className="lbl">NUTANIX SETTINGS</span>
              <span className="lbl lbl-faint" style={{ fontSize: 9 }}>VENDOR-SPECIFIC</span>
            </div>

            <div className="col gap-2">
              <span className="lbl">EXCHANGE RATE  <span style={{ fontWeight: 400, color: 'var(--ink-mute)' }}>· USD → AUD</span></span>
              <div className="sel" style={{ justifyContent: 'flex-start' }}>
                <span className="hand" style={{ fontSize: 14, fontWeight: "200" }}>0.7354</span>
              </div>
            </div>

            <div className="col gap-2">
              <span className="lbl">MARGIN  <span style={{ fontWeight: 400, color: 'var(--ink-mute)' }}>· %, 2 d.p.</span></span>
              <div className="sel" style={{ justifyContent: 'flex-start' }}>
                <span className="hand" style={{ fontSize: 14, fontWeight: "200" }}>5.25</span>
                <span className="hand" style={{ fontSize: 14, color: 'var(--ink-mute)', marginLeft: 'auto' }}>%</span>
              </div>
            </div>

            {/* Derived output template — based on vendor + file type */}
            <div
              style={{
                marginTop: 4,
                padding: '10px 12px',
                borderRadius: 8,
                background: '#ecfdf5',
                border: '1.5px solid #10b981',
                display: 'flex',
                flexDirection: 'column',
                gap: 4
              }}>
              
              <div className="row between" style={{ alignItems: 'baseline' }}>
                <span className="lbl" style={{ color: '#059669' }}>CRM IMPORT TEMPLATE</span>
                <span className="lbl" style={{ color: '#059669', opacity: 0.6, fontSize: 9 }}>AUTO</span>
              </div>
              <div style={{ fontSize: 14, fontWeight: 600, color: '#047857', letterSpacing: '-0.01em' }}>
                Foreign Uplift
              </div>
            </div>

            <div className="stroke-faint" style={{ height: 1, marginTop: 8 }} />

            <button className="btn primary">Upload & parse</button>
            <span
              className="lbl lbl-faint"
              style={{
                textAlign: 'center',
                textTransform: 'none',
                letterSpacing: 0,
                fontWeight: 400,
                fontSize: 11,
                lineHeight: 1.4,
                marginTop: -4,
              }}
            >
              Output will automatically download once completed.
            </span>
          </div>

          {/* Right dropzone + recent uploads */}
          <div className="grow col" style={{ minHeight: 0 }}>
            <WDropzone size="xl" fluid sub="" height={180} />
            <div className="row between mt-2">
              <span className="lbl lbl-faint">DRAG MULTIPLE FILES TO BATCH PARSE</span>
            </div>

            {/* Recent uploads table */}
            <div
              className="stroke col"
              style={{
                marginTop: 20,
                borderRadius: 12,
                background: 'var(--paper)',
                overflow: 'hidden',
                flex: 1,
                minHeight: 0,
              }}
            >
              <div className="row between" style={{ padding: '14px 18px', borderBottom: '1.5px solid var(--ink-faint)', alignItems: 'center', background: '#f8fafc' }}>
                <span className="lbl">RECENT UPLOADS</span>
                <span className="lbl lbl-faint">LAST 5</span>
              </div>

              {/* Column headers */}
              <div
                className="row"
                style={{
                  padding: '10px 18px',
                  borderBottom: '1px solid var(--ink-faint)',
                  background: '#f8fafc',
                  gap: 14,
                }}
              >
                <span className="lbl lbl-faint" style={{ flex: '2 1 0', minWidth: 0 }}>FILE NAME</span>
                <span className="lbl lbl-faint" style={{ flex: '0.9 1 0' }}>VENDOR</span>
                <span className="lbl lbl-faint" style={{ flex: '1.2 1 0' }}>FILE TYPE</span>
                <span className="lbl lbl-faint" style={{ flex: '0.9 1 0', textAlign: 'right' }}>FX RATE</span>
                <span className="lbl lbl-faint" style={{ flex: '0.7 1 0', textAlign: 'right' }}>MARGIN</span>
                <span className="lbl lbl-faint" style={{ flex: '0.7 1 0', textAlign: 'right' }}>WHEN</span>
                <span className="lbl lbl-faint" style={{ width: 76, textAlign: 'right' }}>FILES</span>
              </div>

              {/* Rows */}
              <div className="col" style={{ flex: 1, minHeight: 0, overflow: 'hidden' }}>
                {[
                  { name: 'nutanix-renewal-Q3-acme.pdf',     vendor: 'Nutanix', type: 'Renewal (PDF)',       fx: '0.7354', margin: '5.25%', when: '2m ago' },
                  { name: 'dell-hardware-server-rack.xlsx',  vendor: 'Dell',    type: 'Hardware Only (PDF)', fx: '0.7341', margin: '6.00%', when: '1h ago' },
                  { name: 'lenovo-software-licences.pdf',    vendor: 'Lenovo',  type: 'Software Only (PDF)', fx: '0.7350', margin: '4.50%', when: 'Yesterday' },
                  { name: 'nutanix-renewal-globex.pdf',      vendor: 'Nutanix', type: 'Renewal (PDF)',       fx: '0.7361', margin: '5.25%', when: '2 days ago' },
                ].map((r, i, arr) => (
                <div
                  key={r.name}
                  className="row"
                  style={{
                    padding: '12px 18px',
                    borderBottom: i < arr.length - 1 ? '1px solid var(--ink-faint)' : 'none',
                    alignItems: 'center',
                    gap: 14,
                    fontFamily: 'var(--ui)',
                    fontSize: 13,
                  }}
                >
                  <div className="row" style={{ flex: '2 1 0', minWidth: 0, alignItems: 'center', gap: 8 }}>
                    <span className="lbl" style={{ padding: '2px 5px', border: '1.5px solid var(--ink-mute)', borderRadius: 4, fontSize: 9 }}>
                      {r.name.split('.').pop().toUpperCase()}
                    </span>
                    <span
                      style={{
                        flex: 1,
                        minWidth: 0,
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                        color: 'var(--ink)',
                        fontWeight: 500,
                      }}
                    >
                      {r.name}
                    </span>
                  </div>
                  <span style={{ flex: '0.9 1 0', color: 'var(--ink)', fontWeight: 500 }}>{r.vendor}</span>
                  <span style={{ flex: '1.2 1 0', color: 'var(--ink-soft)' }}>{r.type}</span>
                  <span style={{ flex: '0.9 1 0', textAlign: 'right', color: 'var(--ink-soft)', fontVariantNumeric: 'tabular-nums' }}>{r.fx}</span>
                  <span style={{ flex: '0.7 1 0', textAlign: 'right', color: 'var(--ink-soft)', fontVariantNumeric: 'tabular-nums' }}>{r.margin}</span>
                  <span className="lbl lbl-faint" style={{ flex: '0.7 1 0', textAlign: 'right' }}>{r.when}</span>

                  {/* Action icons */}
                  <div className="row" style={{ width: 76, justifyContent: 'flex-end', gap: 4 }}>
                    <button
                      type="button"
                      title="Download original"
                      style={{
                        width: 28, height: 28, padding: 0,
                        border: '1.5px solid var(--ink-faint)',
                        borderRadius: 6,
                        background: 'var(--paper)',
                        cursor: 'pointer',
                        color: 'var(--ink-soft)',
                        display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                      }}
                    >
                      <svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                        <path d="M14 2v6h6" />
                        <path d="M12 12v6" />
                        <path d="m9 15 3 3 3-3" />
                      </svg>
                    </button>
                    <button
                      type="button"
                      title="Download CRM-ready export"
                      style={{
                        width: 28, height: 28, padding: 0,
                        border: '1.5px solid var(--accent)',
                        borderRadius: 6,
                        background: 'var(--accent-soft)',
                        cursor: 'pointer',
                        color: 'var(--accent)',
                        display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                      }}
                    >
                      <svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                        <path d="m7 10 5 5 5-5" />
                        <path d="M12 15V3" />
                      </svg>
                    </button>
                  </div>
                </div>
              ))}
              </div>

              {/* Pagination footer */}
              <div
                className="row between"
                style={{
                  padding: '10px 18px',
                  borderTop: '1px solid var(--ink-faint)',
                  background: '#f8fafc',
                  alignItems: 'center',
                  flexShrink: 0,
                }}
              >
                <span className="lbl lbl-faint">SHOWING 1 – 4 OF 12</span>
                <div className="row" style={{ alignItems: 'center', gap: 4 }}>
                  <button
                    type="button"
                    disabled
                    style={{
                      width: 28, height: 28, padding: 0,
                      border: '1.5px solid var(--ink-faint)', borderRadius: 6,
                      background: 'var(--paper)', cursor: 'not-allowed',
                      color: 'var(--ink-faint)', opacity: 0.6,
                      display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                    }}
                  >
                    <svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="m15 18-6-6 6-6" />
                    </svg>
                  </button>
                  {[1, 2, 3].map((p) => (
                    <button
                      key={p}
                      type="button"
                      style={{
                        minWidth: 28, height: 28, padding: '0 8px',
                        border: '1.5px solid ' + (p === 1 ? 'var(--ink)' : 'var(--ink-faint)'),
                        borderRadius: 6,
                        background: p === 1 ? 'var(--ink)' : 'var(--paper)',
                        color: p === 1 ? 'var(--paper)' : 'var(--ink-soft)',
                        fontFamily: 'var(--ui)', fontSize: 11, fontWeight: 700,
                        letterSpacing: '0.05em',
                        cursor: 'pointer',
                      }}
                    >
                      {p}
                    </button>
                  ))}
                  <button
                    type="button"
                    style={{
                      width: 28, height: 28, padding: 0,
                      border: '1.5px solid var(--ink-faint)', borderRadius: 6,
                      background: 'var(--paper)', cursor: 'pointer',
                      color: 'var(--ink-soft)',
                      display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                    }}
                  >
                    <svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="m9 18 6-6-6-6" />
                    </svg>
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <WAnno x={48} y={280} w={230}
      text="vendor-specific fields appear inline (Nutanix → FX rate + margin)"
      arrow={<WArrow length={60} />} />
      
    </div>);

}

// ============================================================
//  V5 — Vendor Tile Picker (expanded)
//   Pick-a-vendor cards up top (logo-shaped tiles). Selecting one
//   expands inline to file-type pills + dropzone. Most welcoming.
// ============================================================
function V5_TileVendor() {
  const vendors = [
  { name: 'Nutanix', glyph: 'N', active: true },
  { name: 'Dell', glyph: 'D' },
  { name: 'Lenovo', glyph: 'L' }];

  return (
    <div className="wf wf-stage" style={{ width: 1100, height: 780 }}>
      <WHeader />
      <div className="col center" style={{ paddingTop: 44 }}>
        <div className="scrib" style={{ fontSize: 30, lineHeight: 1.15, fontWeight: 700, letterSpacing: '-0.02em', textAlign: 'center' }}>
          Which vendor is this quote from?
        </div>
        <div className="hand" style={{ fontSize: 15, color: 'var(--ink-soft)', marginTop: 6 }}>
          We'll tailor the parser to their format.
        </div>

        {/* Vendor tiles */}
        <div className="row gap-4 mt-6">
          {vendors.map((v) =>
          <div
            key={v.name}
            className="stroke col center"
            style={{
              width: 180, height: 130,
              borderRadius: 14,
              background: v.active ? 'var(--accent-soft)' : 'var(--paper)',
              borderColor: v.active ? 'var(--accent)' : 'var(--ink)',
              position: 'relative',
              padding: 12
            }}>
            
              <div
              className="stroke col center"
              style={{
                width: 56, height: 56, borderRadius: 12,
                background: 'var(--paper)',
                borderColor: v.active ? 'var(--accent)' : 'var(--ink)'
              }}>
              
                <span className="scrib" style={{ fontSize: 22, fontWeight: 700, color: v.active ? 'var(--accent)' : 'var(--ink)' }}>{v.glyph}</span>
              </div>
              <div className="hand" style={{ fontSize: 15, fontWeight: 600, marginTop: 8 }}>{v.name}</div>
              {v.active &&
            <div style={{ position: 'absolute', top: 8, right: 8, color: 'var(--accent)' }}>
                  <WIcon.check style={{ width: 16, height: 16 }} />
                </div>
            }
            </div>
          )}
        </div>

        {/* Inline expansion — file types + dropzone */}
        <div
          className="stroke col mt-6"
          style={{
            width: 820,
            padding: 24,
            borderRadius: 14,
            background: 'var(--paper)',
            borderStyle: 'solid',
            borderColor: 'var(--ink)',
            position: 'relative'
          }}>
          
          {/* connector tab */}
          <div style={{
            position: 'absolute', top: -9, left: 100, width: 16, height: 16,
            background: 'var(--paper)', borderTop: '1.5px solid var(--ink)', borderLeft: '1.5px solid var(--ink)',
            transform: 'rotate(45deg)'
          }} />

          <span className="lbl">FILE TYPE FOR NUTANIX</span>
          <div className="row gap-2 mt-2">
            <div className="chip active">Renewal (PDF)</div>
            <div className="chip">Hardware Only (PDF)</div>
            <div className="chip">Software Only (PDF)</div>
          </div>

          <div className="mt-4" />
          <WDropzone size="lg" />
        </div>
      </div>

      <WAnno x={820} y={210} w={240}
      text="card picks a vendor, then 'unfolds' the rest"
      arrow={<WArrow length={70} />} />
      
    </div>);

}

// ============================================================
//  V6 — Post-upload progress (state, not a layout direction)
//   Shows V2's composition mid-parse: dropzone replaced inline
//   with a file list + parsing progress. No modal, no nav change.
// ============================================================
function V6_UploadingState() {
  return (
    <div className="wf wf-stage" style={{ width: 1100, height: 780 }}>
      <WHeader />
      <div className="col center" style={{ paddingTop: 44 }}>
        <div className="scrib" style={{ fontSize: 36, lineHeight: 1, fontWeight: 700 }}>
          Bid<span style={{ color: 'var(--accent)' }}>Parser</span>
        </div>

        <div className="row gap-2 mt-6">
          <div className="chip sm active">Nutanix</div>
          <div className="chip sm">·</div>
          <div className="chip sm active">Renewal (PDF)</div>
        </div>

        {/* Dropzone morphs into status panel — same footprint */}
        <div
          className="stroke col mt-6"
          style={{
            width: 760, padding: 22, borderRadius: 16,
            borderStyle: 'solid', borderColor: 'var(--ink)',
            background: 'var(--paper)'
          }}>
          
          <div className="row between" style={{ alignItems: 'center' }}>
            <span className="lbl">PARSING 3 FILES</span>
            <span className="lbl lbl-faint">2 OF 3 DONE</span>
          </div>

          <div className="col gap-2 mt-4">
            <FileRow name="Q1-renewal-acme.pdf" status="done" />
            <FileRow name="Q2-renewal-globex.pdf" status="parsing" pct={62} />
            <FileRow name="Q3-renewal-initech.pdf" status="queued" />
          </div>

          <div className="row between mt-4" style={{ alignItems: 'center' }}>
            <button className="btn ghost">+ Add more files</button>
            <button className="btn primary">Download .xlsx</button>
          </div>
        </div>

        <div className="lbl lbl-faint mt-6">SAFE TO CLOSE — WE'LL EMAIL YOU WHEN IT'S READY</div>
      </div>

      <WAnno x={130} y={330} w={220}
      text="dropzone box becomes the progress panel — same spot"
      arrow={<WArrow length={70} />} />
      
    </div>);

}

function FileRow({ name, status, pct = 0 }) {
  const isDone = status === 'done';
  const isParsing = status === 'parsing';
  return (
    <div className="row gap-3" style={{ alignItems: 'center', padding: '10px 12px', border: '1.5px solid var(--ink-soft)', borderRadius: 10 }}>
      <span className="lbl" style={{ padding: '3px 6px', border: '1.5px solid var(--ink)', borderRadius: 4 }}>PDF</span>
      <span className="hand" style={{ fontSize: 13, flex: 1, color: 'var(--ink)' }}>{name}</span>
      {isDone &&
      <span className="lbl" style={{ color: 'var(--accent)', display: 'inline-flex', alignItems: 'center', gap: 4 }}>
          <WIcon.check style={{ width: 14, height: 14 }} /> PARSED
        </span>
      }
      {isParsing &&
      <div className="row gap-2" style={{ alignItems: 'center', width: 200 }}>
          <div className="bar" style={{ flex: 1 }}><i style={{ width: pct + '%' }} /></div>
          <span className="lbl">{pct}%</span>
        </div>
      }
      {status === 'queued' &&
      <span className="lbl lbl-faint">QUEUED</span>
      }
    </div>);

}

window.V2_PillFilters = V2_PillFilters;
window.V3_WizardStrip = V3_WizardStrip;
window.V4_SidePanel = V4_SidePanel;
window.V5_TileVendor = V5_TileVendor;
window.V6_UploadingState = V6_UploadingState;