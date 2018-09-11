#!/bin/bash

baseName="${COSMOSDB_NAME}"
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

echo "Deploying CosmosDb instances to $rgName in $location"
az group create -g $rgName -l $location

az group deployment create -g $rgName --template-file cosmosdb.json --parameters name=$baseName
echo "Deploy done!"

cdbNames=$(az cosmosdb list -g $rgName | jq 'map(.["name"])' | jq -c '.[]' | tr -d '"');
echo "Creating initial collection & graphs"

for cdb in $cdbNames; do 
  if [[ $cdb = *"-doc-"* ]]; then
    echo "Creating initial collection in $cdb"
    az cosmosdb database create --name $cdb -g $rgName --db-name pets
    az cosmosdb collection create --name $cdb -g $rgName --db-name pets --collection-name checks --throughput 10000
  fi
  if [[ $cdb = *"-graph-"* ]]; then
    echo "Creating empty graph in $cdb"
    az cosmosdb database create --name $cdb -g $rgName --db-name pets
    az cosmosdb collection create --name $cdb -g $rgName --db-name pets --collection-name checks --throughput 10000
  fi
done

echo "Displaying keys"
for cdb in $cdbNames; do 
  echo "Keys for $cdb"
  az cosmosdb list-keys -g $rgName --name $cdb;
done


