@echo off
rem Deploys the app's Firestore rules (database "(default)" only, see firebase.json) and the
rem app functions changed for the web-panel split. paymongoWebhook is deliberately left
rem out: the live one is the web panel's, and deploying the app's would replace it.
cd /d "%~dp0.."
call firebase deploy --project nutrade-a25c7 --only "firestore:rules,functions:onListingUpdated,functions:approveListing,functions:rejectListing,functions:createQrPayment,functions:checkListingPayment,functions:expireStalePayments"
