import { Icon, type IconName } from "../icons/Icon";
export function NavItem({href,label,icon,active=false}:{href:string;label:string;icon:IconName;active?:boolean}) {
  return <a className={active ? "active" : ""} href={href}><Icon name={icon}/><span>{label}</span></a>;
}
