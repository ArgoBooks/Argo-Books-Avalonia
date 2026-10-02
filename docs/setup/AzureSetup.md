# Azure Setup

This guide walks through everything you need to set up in Microsoft Azure to build, ship, and maintain Argo Books. It assumes you have nothing configured yet.

Argo Books uses Azure for two things:

1. **Azure Translator** - used by `ArgoBooks.Translations` to translate UI strings into every supported language
2. **Azure Artifact Signing** - used to digitally sign the Windows `.exe` installer produced by Advanced Installer

Azure is not used by Argo Books day to day. It's only used to generate translations or publish a signed Windows release.

## Prerequisites

1. A Microsoft account (any `@outlook.com`, `@hotmail.com`, or other Microsoft-linked email works)
2. A bank card for billing

## Part 1: Account setup

### Step 1: Create the Azure subscription

You need a **paid** subscription. Free trial and sponsored subscriptions do not work for Artifact Signing, so go straight to Pay-As-You-Go even if Microsoft offers you a $200 free trial credit on signup.

1. Go to <https://portal.azure.com> and sign in with your Microsoft account
2. If this is your first time, Azure will prompt you to sign up.
3. Click **Subscriptions** > **+ Add**
3. Choose **Pay-As-You-Go** as the offer type
4. Fill in:
   - **Account information:** your legal name, address, and phone number. This is what will appear on signed installers later, so make sure it's exactly how you want it shown.
   - **Identity verification:** confirm via SMS or call
   - **Payment method:** add a bank card. You will not be charged anything until you actually use a billed service.
5. Accept the agreement and finish signup

You'll land on a new subscription, usually named **"Subscription 1"**. You can rename it under **Subscriptions** > your subscription > **Rename** if you want.

#### Verify the billing profile

After the subscription is made, double-check the billing profile, since the legal name on it gets baked into the signing certificate later:

1. **Subscriptions** > your subscription > **Billing**
2. Confirm the legal name and address are exactly what you want appearing on signed binaries

### Step 2: Create the resource group

Both of the Azure services we use will live in the same resource group.

1. In the Azure portal, go to **Resource groups**
2. Click **+ Create**
3. Fill in:
   - **Subscription:** the subscription you created above
   - **Resource group:** `ArgoBooks`
   - **Region:** `Canada Central`
4. **Review + create**, then **Create**

## Part 2: Azure Translator

Used by the translation tool to generate `fr.json`, `de.json`, etc. from English source strings. Set this up on the **F0 (free) tier**, which covers 2 million characters per month. That's more than enough for our use. A new version of Argo Books usually only adds a few thousand characters of new text.

### Step 3: Create the Translator resource

1. In the Azure portal, click **Create a resource**
2. Search for **Translator** and select the one published by Microsoft
3. Click **Create** and fill in:
   - **Subscription:** the subscription you created above
   - **Resource group:** `ArgoBooks`
   - **Region:** `Canada Central` (any region works as long as you set `AZURE_TRANSLATOR_REGION` to match, when you use `ArgoBooks.Translations`)
   - **Name:** `ArgoBooks-Translator`
   - **Pricing tier:** Choose **Free F0**. Free as long as you stay under 2M characters per month.
4. **Review + create**, then **Create**

### If you ever need more than 2M chars/month

This only happens if you regenerate every language from scratch, which should never be necessary. When that happens:

1. Delete the F0 resource (Overview > **Delete**)
2. Create a new resource on **Standard S1** with the same name and region
4. Run the full re-translation. S1 is \$10 USD per 1M characters, so a complete retranslation across all supported languages would probably cost around \$20 (I'm guessing).
5. After the run, delete the S1 resource and recreate it on F0 again to save yourself a couple dollars.

### Step 4: Get the key and region

1. Open the new `ArgoBooks-Translator` resource
2. In the left sidebar, click **Keys and Endpoint**
3. Copy **Key 1** and note the **Location/Region** (e.g. `canadacentral`)

You will need these to run the translation tool. See [ArgoBooks.Translations/README.md](../../tools/ArgoBooks.Translations/README.md) for usage, or [Localization.md](../Localization.md) for the full picture of how localization works in Argo Books.

## Part 3: Azure Artifact Signing

Used to sign the Windows installer. Pricing is $9.99 USD/month on the Basic tier.

### Step 6: Match your billing profile to the cert subject

This is the single most important preparation step. The certificate's "subject" (the legal name and address that appears in signed binaries) is pulled directly from your Azure billing profile, not from a form. If your billing profile says `Evan` but you want `Evan DiPlacido` on the cert, fix the billing profile first.

1. Go to **Subscriptions** > your subscription > **Billing profile**
2. Verify the legal name and address are exactly what you want shown on signed installers
3. Save any changes before proceeding

### Step 7: Register the resource provider

1. Go to **Subscriptions** > your subscription > **Resource providers**
2. Search for `Microsoft.CodeSigning`
3. Click **Register**

### Step 8: Create the Artifact Signing account

1. **Create a resource** > search for **Artifact Signing**. If it does not appear, then search for it in the search bar in the website's header.
2. Click **Create** and fill in:
   - **Subscription:** the subscription you created above
   - **Resource group:** `ArgoBooks`
   - **Region:** `East US`(limited regions are available; pick the closest one)
   - **Name:** `ArgoBooks-Signing`
   - **Pricing tier:** **Basic ($9.99 USD/month)**
3. **Review + create**, then **Create**

### Step 9: Grant yourself the required roles

Even as the resource owner, you need two explicit role assignments:

- **Artifact Signing Identity Verifier** - required to submit identity validation
- **Artifact Signing Certificate Profile Signer** - required to actually sign binaries

Assign each one with this flow:

1. Open the new `ArgoBooks-Signing` account
2. **Access control (IAM)** > **+ Add** > **Add role assignment**
3. In the role search, type the role name and click on it to select it
4. Click **Next**
5. Make sure **User, group, or service principal** is selected under "Assign access to"
6. Click **+ Select members**, find your own account, select it, then click **Select**
7. Click **Review + assign**, then **Review + assign** again to confirm

### Step 10: Complete identity validation

1. In `ArgoBooks-Signing`, open **Identity Validations** in the left sidebar
2. Make sure the dropdown says **Individual**
3. Click **+ New identity** and submit the request
4. A couple minutes later, it should say 'Action Required'. Follow the instructions. Microsoft uses a third-party service called **AU10TIX** for the actual photo ID / selfie check.

Tip: If Microsoft Azure has problems with the login system, use Google Chrome. Ironically it didn't work in Microsoft edge for me.

> **Expect a delay after AU10TIX.** Once you finish the AU10TIX flow, the Azure portal may keep showing "Action Required" for several minutes. Don't click the link again. Just wait for the email saying validation is complete.

You cannot create a certificate profile until validation succeeds.

### Step 11: Create the certificate profile

Once validation is approved:

1. In `ArgoBooks-Signing`, open **Certificate Profiles**
2. Click **+ Create**
3. **Profile type:** Public Trust
5. **Profile name:** `ArgoBooks-prod`
4. **Identity validation:** select the validation you just completed
6. Create

Note this name. Advanced Installer needs it.

### Step 12: Install the client tooling

On your build machine, install the Trusted Signing client tools:

```powershell
winget install -e --id Microsoft.Azure.TrustedSigningClientTools
```

If Azure CLI is not already installed:

```powershell
winget install Microsoft.AzureCLI
```

Then:

```powershell
az login
```

So Advanced Installer can pick up your credentials.

> **If `az login` fails with "No subscriptions found":** this usually means your Microsoft account is also linked to a school or work tenant, and the CLI is trying the wrong tenants first and hitting MFA. Point it directly at your personal Azure tenant instead:
>
> ```powershell
> az login --tenant <your-tenant>.onmicrosoft.com
> ```
>
> You can find your tenant domain in the Azure portal URL after `#@` (e.g. `https://portal.azure.com/#@evandiplacidooutlook.onmicrosoft.com/...`).

### Step 13: Create the metadata JSON

The signing command in the next step reads a JSON file with your Trusted Signing endpoint, account name, and certificate profile name. This is what tells `signtool` which Azure cert to use.

Create the file at `packaging/windows/trusted-signing-metadata.json` in this repo with these contents:

```json
{
  "Endpoint": "https://eus.codesigning.azure.net/",
  "CodeSigningAccountName": "ArgoBooks-Signing",
  "CertificateProfileName": "ArgoBooks-prod"
}
```

- **Endpoint:** the regional endpoint for your Trusted Signing account. If you used `East US` in Step 8 (the default), keep `https://eus.codesigning.azure.net/`. For other regions, look up the endpoint at https://learn.microsoft.com/azure/trusted-signing/concept-trusted-signing-resources-roles.
- **CodeSigningAccountName:** the account name from Step 8 (`ArgoBooks-Signing`).
- **CertificateProfileName:** the profile name from Step 11 (`ArgoBooks-prod`).

None of these values are secrets. The real authentication happens via your `az login` token at sign time.

### Step 14: Configure Advanced Installer and verify the signed build

The Advanced Installer signing config and the end-to-end verification live in this repo, alongside the project file at `packaging/windows/Argo Books.aip`. See [Advanced Installer project setup.md](Advanced%20Installer%20project%20setup.md), specifically the **Digital Signature** step and the **Verify a build** step.

For the full publishing workflow (Windows, macOS, and Linux), see [Publishing.md](../Publishing.md).

## Ongoing costs

| Service | Tier | Approx. cost                                                                                          |
|---------|------|-------------------------------------------------------------------------------------------------------|
| Azure Translator | F0 (free) by default | \$0 up to 2M chars/month. Temporarily switch to S1 for full re-translations (~\$10 USD per 1M chars). |
| Azure Artifact Signing | Basic | \$9.99 USD/month (~\$164 CAD/year), 5,000 signatures/year                                             |
