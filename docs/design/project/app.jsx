// Mount only V4 (Side Panel) onto the DesignCanvas.
// V1/V2/V3/V5/V6 are still defined in scenes.jsx / variants.jsx but
// no longer placed on the canvas — focusing iteration on V4.

const { V4_SidePanel } = window;

function App() {
  return (
    <DesignCanvas>
      <DCSection
        id="side-panel"
        title="BidParser — Side Panel direction"
        subtitle="Settings card on the left (vendor → file type → vendor-specific fields), large dropzone on the right."
      >
        <DCArtboard id="v4" label="V4 · Side panel" width={1280} height={900}>
          <V4_SidePanel />
        </DCArtboard>
      </DCSection>
    </DesignCanvas>
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(<App />);
