export function VendorSelect({ vendors, value, onChange }: { vendors: string[]; value: string; onChange: (value: string) => void }) {
  return (
    <label className="flex flex-col gap-2">
      <span className="label">Vendor</span>
      <select className="field appearance-none" value={value} onChange={(event) => onChange(event.target.value)}>
        <option value="">Select vendor</option>
        {vendors.map((vendorName) => (
          <option key={vendorName} value={vendorName}>
            {vendorName}
          </option>
        ))}
      </select>
    </label>
  );
}
