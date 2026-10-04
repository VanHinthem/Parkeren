# Inloggen en sessies

Een gebruiker logt in met een unieke gebruikersnaam en een PIN van zes cijfers. Alleen actieve accounts kunnen inloggen of bestaande sessies gebruiken; deactiveren en archiveren blokkeren beide. Een succesvolle login kan op een vertrouwd apparaat persistent blijven; V1 gebruikt hiervoor een sessie van maximaal 30 dagen.

De PIN is niet uitleesbaar door de applicatie of beheerder. Een gedeactiveerde gebruiker kan niet inloggen en verliest ook toegang via bestaande sessies.

Een ingelogde gebruiker kan de eigen PIN wijzigen nadat de huidige PIN is gecontroleerd. De huidige sessie blijft daarbij actief; andere actieve sessies worden ingetrokken.

Een beheerder kan een nieuwe of tijdelijke PIN instellen zonder de bestaande PIN te kunnen zien. Bij zo'n reset worden alle actieve sessies van de betreffende gebruiker ingetrokken. De beheerder kan ook los daarvan alle sessies van een gebruiker intrekken.

Uitloggen beëindigt uitsluitend de sessie op het huidige apparaat.
