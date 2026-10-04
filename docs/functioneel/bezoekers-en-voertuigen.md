# Bezoekers en voertuigen

Een beheerder kan gebruikers en voertuigen aanmaken, activeren/deactiveren en archiveren. Archiveren is definitief voor de status: een gearchiveerd record kan niet opnieuw worden geactiveerd. Archiveren is toegestaan zodra eventuele actieve Visits veilig zijn beëindigd.

Voertuigen en gebruikers hebben een many-to-manyrelatie: één gebruiker kan meerdere kentekens krijgen en hetzelfde voertuig kan aan meerdere gebruikers worden toegewezen. Een bezoeker kan deze toewijzingen niet zelf wijzigen.

Een ingelogde gebruiker krijgt via de backend uitsluitend actieve voertuigen terug die aan het eigen account zijn toegewezen. De client is daarmee niet de autoritatieve bron voor voertuigautorisatie.

Deactiveren en archiveren blokkeren login of nieuwe Visits. Archiveren behoudt Visits, provideracties, verbruik, kosten en de koppelingen die historische rapportages nodig hebben. Een gebruiker of voertuig met parkeerhistorie kan niet fysiek worden verwijderd. Permanent verwijderen is alleen mogelijk wanneer geen parkeerhistorie bestaat; operationele sessies en toewijzingen worden daarbij opgeruimd.
