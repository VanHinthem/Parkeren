# Authenticatie en sessies

## V1-baseline

Parkeren gebruikt een unieke gebruikersnaam en een PIN van exact zes cijfers. De PIN wordt nooit opgeslagen; alleen een ASP.NET `PasswordHasher<TUser>`-compatibele hash staat in PostgreSQL.

Na een succesvolle login genereert de server 32 cryptografisch willekeurige bytes. Alleen een SHA-256-hash van dit sessietoken wordt in `user_sessions` opgeslagen. De browser ontvangt het token uitsluitend via een `Secure`, `HttpOnly`, `SameSite=Strict` cookie. De sessie is standaard 30 dagen geldig.

Iedere authenticatiecontrole valideert zowel de sessie als `User.IsActive`. Een gedeactiveerde gebruiker verliest daardoor direct API-toegang, ook wanneer een bestaande sessie nog niet verlopen is.

Login is rate-limited op de API. De huidige baseline is vijf requests per minuut per limiter-partitie; vóór productie wordt de partitionering samen met reverse-proxy/client-IP gedrag expliciet gehard en getest.

## Endpoints

- `POST /api/auth/login` — valideert gebruikersnaam/PIN, maakt een server-side sessie en zet de cookie.
- `GET /api/auth/me` — retourneert de actuele ingelogde gebruiker wanneer sessie én gebruiker geldig zijn.
- `POST /api/auth/logout` — trekt uitsluitend de huidige sessie in en verwijdert de cookie.

## Nog in Phase 2

Admin user management, PIN wijzigen/resetten, alle sessies intrekken, CSRF-bescherming voor mutaties, Vehicle/UserVehicle en volledige autorisatie volgen in de volgende Phase-2-slices.
