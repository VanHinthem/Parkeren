# Eerste beheerder

Bij startup voert de API eerst de EF Core-migraties uit. Als daarna nog geen enkele gebruiker bestaat, is een eenmalige bootstrap-configuratie verplicht.

Configuratie:
- `BootstrapAdmin__Username`
- `BootstrapAdmin__Pin` — exact zes cijfers

In Docker Compose worden deze gevuld vanuit `PARKEREN_BOOTSTRAP_ADMIN_USERNAME` en `PARKEREN_BOOTSTRAP_ADMIN_PIN`.

De bootstrap draait uitsluitend wanneer de tabel `users` leeg is. De PIN wordt direct met de normale ASP.NET password hasher opgeslagen en wordt niet gelogd. Zodra de beheerder bestaat hebben de bootstrapwaarden geen effect meer. Verwijder ze daarna uit de runtime-`.env`.

Wanneer de database leeg is en geldige bootstrapconfiguratie ontbreekt, start de applicatie bewust niet. Daardoor kan een installatie niet ongemerkt zonder beheeraccount beschikbaar komen.
