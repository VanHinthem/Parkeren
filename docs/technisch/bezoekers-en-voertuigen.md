# Bezoekers en voertuigen

## Datamodel

`Vehicle` bewaart het getoonde kenteken en een genormaliseerde unieke variant. `UserVehicle` gebruikt `(UserId, VehicleId)` als samengestelde primary key, waardoor de many-to-manytoewijzing ook op databaseniveau uniek is.

Foreign keys gebruiken `Restrict`; gebruikers, voertuigen en toewijzingen worden niet via cascade verwijderd. Dit ondersteunt de latere historische Visit-invarianten.

## Autorisatie

Muterend beheer loopt via `IAdministrationService` en vereist een actieve gebruiker met de rol `Admin`. De visitor-endpoint voor voertuigen leidt de gebruiker uitsluitend af uit de server-side sessie en retourneert alleen actieve, toegewezen voertuigen.

De daadwerkelijke Visit-start zal later opnieuw `vehicleId` server-side valideren; het feit dat een voertuig eerder door de UI is opgehaald is nooit voldoende autorisatie.
