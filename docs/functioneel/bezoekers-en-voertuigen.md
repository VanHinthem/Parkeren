# Bezoekers en voertuigen

Een beheerder kan gebruikers en voertuigen aanmaken en activeren/deactiveren. Een nieuw gebruikersaccount krijgt niet automatisch een voertuig.

Voertuigen en gebruikers hebben een many-to-manyrelatie: één gebruiker kan meerdere kentekens krijgen en hetzelfde voertuig kan aan meerdere gebruikers worden toegewezen. Een bezoeker kan deze toewijzingen niet zelf wijzigen.

Een ingelogde gebruiker krijgt via de backend uitsluitend actieve voertuigen terug die aan het eigen account zijn toegewezen. De client is daarmee niet de autoritatieve bron voor voertuigautorisatie.

Archiveren/permanent verwijderen en de aanvullende regels rond bestaande Visit-historie worden toegevoegd wanneer de Visit-persistence in Phase 5 beschikbaar is; de huidige Phase-2-basis ondersteunt activeren/deactiveren zonder records te verwijderen.
