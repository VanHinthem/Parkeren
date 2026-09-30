import "./LicensePlate.css";

type Props={value:string};

export function LicensePlate({value}:Props){
  return <span className="license-plate" aria-label={`Kenteken ${value}`}>
    <span className="license-plate__nl">NL</span>
    <strong>{value}</strong>
  </span>;
}
