# Authenticatie en sessies

## V1-baseline

Parkeren gebruikt een unieke gebruikersnaam en een PIN van exact zes cijfers. De PIN wordt nooit opgeslagen; alleen een ASP.NET `PasswordHasher<TUser>`-compatibele hash staat in PostgreSQL.

Na een succesvolle login genereert de server 32 cryptografisch willekeurige bytes. Alleen een SHA-256-hash van dit sessietoken wordt in `user_sessions` opgeslagen. De browser ontvangt het token uitsluitend via een `HttpOnly`, `SameSite=Strict` cookie. Buiten de lokale Development-omgeving is de cookie ook `Secure`. De sessie is standaard 30 dagen geldig.

Iedere authenticatiecontrole valideert zowel de sessie als `User.IsActive`. Een gedeactiveerde gebruiker verliest daardoor direct API-toegang, ook wanneer een bestaande sessie nog niet verlopen is.

Login is rate-limited op de API: vijf requests per minuut per door ASP.NET waargenomen client-IP. Vóór productie wordt dit samen met forwarded headers/reverse-proxy gedrag expliciet gehard en getest.

## PIN- en sessiebeheer

Een gebruiker kan zijn eigen PIN alleen wijzigen na verificatie van de huidige PIN. Na wijzigen blijft de huidige sessie actief en worden andere actieve sessies ingetrokken.

Een actieve beheerder kan voor een gebruiker een nieuwe/tijdelijke PIN instellen zonder de oude PIN te kunnen uitlezen. Een admin-reset trekt alle actieve sessies van die gebruiker in. Een beheerder kan daarnaast expliciet alle sessies van een gebruiker intrekken.

## Endpoints

- `POST /api/auth/login` — login en nieuwe server-side sessie.
- `GET /api/auth/me` — actuele gebruiker.
- `POST /api/auth/logout` — alleen huidige sessie intrekken.
- `POST /api/auth/change-pin` — eigen PIN wijzigen; huidige PIN vereist.
- `POST /api/admin/users/{userId}/reset-pin` — admin stelt nieuwe PIN in en trekt alle sessies in.
- `POST /api/admin/users/{userId}/revoke-sessions` — admin trekt alle sessies in.

## Nog in Phase 2

CSRF-bescherming voor mutaties wordt toegevoegd voordat deze endpoints via de uiteindelijke UI worden gebruikt. Admin user management, Vehicle/UserVehicle en volledige autorisatie volgen in de volgende Phase-2-slices.
