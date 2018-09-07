#!/bin/bash

# Creates an Azure KeyVault service
# Requirement:
#   Set Environment Variables defined below
# Environment Variables:
#	KEYVAULT_NAME: Name of the KeyVault service
#   AF_RG: Name of the resource group
#   AF_REGION: location
# Usage:
#	execute ./deploy-keyvault.sh

KeyVaultName="${KEYVAULT_NAME}"
afRg="${AF_RG}"
afRegion="${AF_REGION}"

echo "------------------------------------------------------------"
echo "Creating the KeyVault $KeyVaultName" in rg $afRg
echo "------------------------------------------------------------"
az provider register -n Microsoft.KeyVault
az keyvault create --name $KeyVaultName --resource-group $afRg --location $afRegion