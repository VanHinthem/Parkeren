# Parkeerbeleid

Fase 3 introduceert een expliciet onderscheid tussen gebruikersbeleid en gemeentelijke/providerregels.

## Gebruikersbeleid
`DefaultParkingPolicy` bevat de standaardwaarden. `UserPolicyOverride` bevat uitsluitend expliciete afwijkingen per gebruiker. `ParkingPolicyResolver` berekent daaruit een `EffectiveParkingPolicy`.

De eerste velden zijn `MaxPaidParkingDuration`, optionele `MaxVisitElapsedDuration` en `AllowAutoExtension`. `MaxProviderActionDuration` hoort bewust niet bij gebruikersbeleid; dit wordt onderdeel van de versioned `ParkingRuleSet`.

Bij het starten van een Visit wordt later een immutable snapshot van de effective policy opgeslagen. Wijzigingen in defaults/overrides mogen een reeds actieve Visit dus niet achteraf veranderen.

## Tijd
Absolute Visit/provider-tijden worden als UTC instants opgeslagen. Lokale parkeerregels worden in `Europe/Amsterdam` geëvalueerd. Deze scheiding wordt in de volgende Phase-3 slice uitgewerkt met rulesets en paid/free segmentering.
