var credential = {
	"@id": "https://.../graph/ce-abcdef",
	"@graph": [
		{
			"@id": "https://.../resources/ce-abcdef",
			"@type": "ceterms:Certificate",
			"ceterms:name": { "en": "My certificate" },
			"ceterms:description": { "en": "Description for my certificate" },
			"ceterms:offeredBy": [
				"https://.../graph/ce-mnopqrs"
			],
			"ceterms:requires": [
				{
					"@type": "ceterms:ConditionProfile",
					"ceterms:description": { "en": "Requirements for this certificate" },
					"ceterms:targetAssessment": [
						"https://.../graph/ce-ghijkl",
						"_:12345"
					]
				}
			]
		},
		{
			"@id": "_:12345",
			"@type": "ceterms:AssessmentProfile",
			"ceterms:name": { "en": "Blank Node Assessment" }
		}
	]
}

var assessment = {
	"@id": "https://.../graph/ghijkl",
	"@graph": [
		{
			"@id": "https://.../resources/ce-ghijkl",
			"@type": "ceterms:AssessmentProfile",
			"ceterms:name": { "en": "My Assessment" },
			"ceterms:offeredBy": [
				"https://.../graph/ce-mnopqrs"
			]
		}
	]
}

var organization = {
	"@id": "https://.../graph/mnopqrs",
	"@graph": [
		{
			"@id": "https://.../resources/mnopqrs",
			"@type": "ceterms:CredentialOrganization",
			"ceterms:name": { "en": "My Organization", "fr": "Mon organisation" }
		}
	]
}

//Things we don't actually use in our schema/data, because we don't hate ourselves this much
var edgeCaseTortureTest = {
	"@id": "https://.../graph/test",
	"@graph": [
		{
			"@id": "https://.../resources/test",
			"@type": "fake:EdgeCaseTest",
			"edgeCaseTest:multiDimensionalArray": [
				[ "a", "b", "c" ],
				[ "d", "e", "f" ]
			],
			"edgeCaseTest:mixedTypeArray": [
				"g", "h", { "test": "value" }
			],
			"edgeCaseTest:mixedMultiArray": [
				[ "i", "j", { "test2": "value2" } ],
				[ { "test3": "value3" }, 1, true, 10.5 ],
				"string value",
				100
			]
		}
	]
}

function JSONtoTriples(source){
	//First, ensure every single object has an @id
	//For non-top-level objects (e.g. embedded profiles, language maps, etc) this is the same process as assigning RDF blank node IDs
	//The result will be a completely flat array of objects where each object maintains some reference back to where it belongs
	//It would be possible to reconstruct the JSON hierarchy by running this process in reverse
	for(var i in source){
		assignIdentifiers(source[i], source["@id"]);
	}
	
	//Sanity check - Everything should now have @ids
	console.log("Verify that everything has @ids:", source);
	
	//Next, flatten the JSON completely
	//Replace objects with references to those objects (using the @ids that either already exist or were just assigned)
	var flattenedList = [];
	var startingObject = {};
	flattenedList.push(startingObject);
	for(var i in source){
		flattenJSON(i, source[i], startingObject, flattenedList);
	}
	
	//Sanity check - We should now have a completely flat array of JSON objects
	//No object should contain another object, but arrays of values (e.g. strings) are normal
	console.log("Verify that this is totally flat", flattenedList);
	
	//At this point we have an array of completely flat JSON objects
	//This could be branched off for other purposes, like Neo4j, since it uses the same kind of structure
	//But next, let's build our triples
	//Triples are basically just a big flat list too, but compressed to the flattest possible expression of the data that could still be reconstructed back into the original
	//The @id gets used as the rdf:subject of each triple
	//The property gets used as the rdf:predicate of each triple
	//The value gets used as the rdf:object of each triple
	//The twist here is that arrays are also condensed in a manner comparable to XML or to HTTP GET parameters (the property is repeated for each value)
	//This is what we needed all those generated/assigned @ids for
	var triples = [];
	flattenedList.forEach(function(item){
		buildTriples(item, triples);
	});
	
	//Sanity check - We should now have a pancake-flat list of RDF triples
	console.log("Verify that this is now a set of RDF triples", triples);
	
	//Now you should be able to return the entire document (in terms of the data, not the structure) for a given @id by:
	//First, query for all triples where the rdf:predicate is "@root" and the rdf:object is the @id you're looking for
	//Next, For each of those triples, query for all triples that have the same rdf:subject
	//Otherwise you'd have to start with the @id and recursively query triples as you found more and more references
	//Injecting a @root also lets you easily figure out which parts of the data belong to which originally-published document when you start dealing with multiple documents
	
	//You can also reconstruct the JSON by walking the triples one by one and rebuilding the hierarchy
	//Note that this will cause things like the Assessment Profile Blank Node in the example Credential data to appear inside of the targetAssessment array instead of staying out in the @graph
	//But that's something that can be compensated for
	
	//Anyway, now, return the data
	return triples;
}

function assignIdentifiers(jsonToken, rootDocumentID, parentArray, index){
	//Arrays
	if(Array.isArray(jsonToken)){
		//A potential edge case exists where an array directly contains one or more arrays. We don't use this in our data anywhere, but we should account for it
		if(parentArray){
			//Create a blank node and assign the array as its @value
			var replacement = { "@id": "_:" + generateGUID(), "@type": "@list", "@root": rootDocumentID, "@value": jsonToken };
			//Replace the item in the parent array with the new blank node
			parentArray[index] = replacement;
			//Process the array like normal
			replacement["@value"].forEach(function(item, itemIndex){
				assignIdentifiers(item, rootDocumentID, replacement["@value"], itemIndex);
			});
		}
		else{
			//Arrays can contain objects and/or value types, so just pass each one to this method individually
			jsonToken.forEach(function(item, index){
				assignIdentifiers(item, rootDocumentID, jsonToken, index);
			});
		}
	}
	//Objects
	else if(typeof(jsonToken) == "object"){
		//Only assign an @id if one does not already exist
		//Note that using GUIDs here instead of integers means every object, top-level or not, across the entire registry will be uniquely identifiable
		if(!jsonToken["@id"]){
			jsonToken["@id"] = "_:" + generateGUID();
		}
		
		//Bonus points: Also store a reference to the root document's ID
		//There is no such thing as @root in JSON-LD, but I'm going to use it anyway in this example
		//This would be used for internal database purposes, allowing you to quickly reference/query/delete every node for a given ID
		//You could also use a CTID instead, if you extract it from the root document's @id as the first step in the JSONtoTriples method
		//Always assign this, even to the root document itself, so that you can get everything with one query to this property
		jsonToken["@root"] = rootDocumentID;
		
		//Now recursively call this for the properties of the object
		for(var i in jsonToken){
			assignIdentifiers(jsonToken[i], rootDocumentID);
		}
	}
	//Values
	else{
		//Only objects need IDs, so don't do anything for value types (string, int, bool, etc)
	}
}

function flattenJSON(property, jsonToken, flatObject, flattenedList, asArray){
	//Arrays
	if(Array.isArray(jsonToken)){
		//Array handling needs to account for:
		//- Arrays of objects: these get turned into arrays of references using the objects' @ids
		//- Arrays of values: these (ultimately) get copied verbatim, but each thing in the array has to be checked
		//- Arrays of mixed data (objects and values): these get turned into arrays of values (values copied directly; objects replaced with references)
		//- Arrays with just one item: We need to ensure we don't accidentally turn these into single values (these should still be arrays in the end)
		//If we're careful, we can do all of this by just calling this method again for each item in the array
		jsonToken.forEach(function(item){
			flattenJSON(property, item, flatObject, flattenedList, true);
		});
	}
	//Objects
	else if(typeof(jsonToken) == "object"){
		//Create and store a new container for this object's data
		var newObject = {};
		flattenedList.push(newObject);
		//Recursively call this method, using the new object but the same flattenedList
		for(var i in jsonToken){
			flattenJSON(i, jsonToken[i], newObject, flattenedList);
		}
		//Store a reference to the newly generated object in the property that object would otherwise be the value of
		//Ensure that arrays are still arrays in the end
		//Note that this is different from the handler for Values because we're storing a reference here, not the object itself
		if(!flatObject[property]){
			flatObject[property] = asArray ? [newObject["@id"]] : newObject["@id"];
		}
		else{
			flatObject[property].push(newObject["@id"]);
		}
	}
	//Values
	else{
		//If the property doesn't exist on the object, create it
		if(!flatObject[property]){
			flatObject[property] = asArray ? [jsonToken] : jsonToken;
		}
		//Otherwise, append it - the create step should take care of ensuring that this is already an array
		else{
			flatObject[property].push(jsonToken);
		}
	}
}

function buildTriples(jsonObject, triples){
	for(var i in jsonObject){
		(function(rdfSubject, rdfPredicate, rdfObject){
			//Skip the @id property itself
			if(rdfPredicate == "@id"){
				return;
			}
			//Handle arrays
			else if(Array.isArray(rdfObject)){
				rdfObject.forEach(function(value){
					triples.push(makeTriple(rdfSubject, rdfPredicate, value));
				});
			}
			//Handle other values
			else{
				triples.push(makeTriple(rdfSubject, rdfPredicate, rdfObject));
			}
		})(jsonObject["@id"], i, jsonObject[i]);
	}
}
function makeTriple(rdfSubject, rdfPredicate, rdfObject){
	return {
		"rdf:subject": rdfSubject,
		"rdf:predicate": rdfPredicate,
		"rdf:object": rdfObject
	};
}

function generateGUID(){
	//Javascript doesn't have a built-in GUID generator, but let's pretend it does
	//Borrowed from here for the sake of time: https://www.w3resource.com/javascript-exercises/javascript-math-exercise-23.php
	//Can't vouch for its validity as a truly unique UUID but it should be good enough for this example
	var date = new Date().getTime();
	var uuid = 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
        var r = (date + Math.random()*16)%16 | 0;
        date = Math.floor(date/16);
        return (c=='x' ? r :(r&0x3|0x8)).toString(16);
    });
	return uuid;
}
