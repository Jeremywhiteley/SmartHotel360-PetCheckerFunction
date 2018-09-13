#!/bin/bash

baseName="${STORAGE_NAME}"
rgName="${AF_RG:-sh360-demo}"
location="${AF_REGION:-eastus}"

while [ "$1" != "" ]; do
    case $1 in
        -n | --name)                    shift
                                        baseName=$1
                                        ;;
        -g | --resource-group)          shift
                                        rgName=$1
                                        ;;
        -l | --location)                shift
                                        location=$1
                                        ;;
       * )                              echo "Invalid param. Use just mandatory -n (or --name). Also can use -l (or --location) and -g (or --resource-group)"
                                        exit 1
    esac
    shift
done

 if [[ "$baseName" == "" ]]
 then
    echo "No name is specified. Aborting."
    exit 1
 fi

echo "Deploying Storage instance to $rgName in $location"
az group create -g $rgName -l $location
az group deployment create -g $rgName --template-file storage.json --parameters name=$baseName
echo "Deploy done!"

storageNames=$(az storage account list -g $rgName | jq 'map(.["name"])' | jq -c '.[]' | tr -d '"');
echo "Creating container 'pets' with public access"
for storage in $storageNames; do 
    echo "Creating container in $storage"
    az storage container create --name pets --public-access blob --account-name $storage
done
echo "Displaying keys"
for storage in $storageNames; do 
  echo "Keys for $storage"
  az storage account keys list -g $rgName --account-name $storage
done


