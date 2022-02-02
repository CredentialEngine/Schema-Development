This folder contains examples of CTDL used in W3C Verifiable Credentials, useful for Learning-Employment Records, IMS Open Badges 3.0, Comprehensive Learner Records etc.

## Related:

* Relevant [issue in IMS OB 3.0 development repo](https://github.com/IMSGlobal/openbadges-specification/issues/330).
* Relevant [issue in W3C VC-Edu Use cases repo](https://github.com/w3c-ccg/vc-ed-use-cases/issues/6)
* (open) [discussion paper](https://docs.google.com/document/d/1Ei7uxTeLs_vQPtMpRPx4KVZ-nQ46ySUlXpF1nzhgJUM/edit?usp=sharing) in Google Docs (restricted [version for CredEng team](https://docs.google.com/document/d/1Vp44CutM0oTKNN5gp1DKPe5sJri9rDfh5ItmFLokovg/edit#heading=h.i33feeiez3el))

## Here:
Samples not examples: the code here shows some of what CTDL can do, they are samples of what is possible, not complete examples.

### Sample VCs asserting Pat has a Degree:
CTDL can be used to describe the Credential that the subject has been awarded.

* [s1_1_DegreeInRegistry.json](s1_1_DegreeInRegistry.json): in this example, nearly all the data lives in the Credential Registry, which is great for keeping the size of the VC down, not so good if you need the data and cannot contact the Registry API. Also, there is a risk that this data might vary over time, and it provides no specific data about any options Pat may have taken in earning the credential.
* s1_2_DegreeInRegistryAndVC.json: We keep the id of the credential as a link to the Registry _and_ add the information that we will need to the VC. This mitigates the risks in the first example, but increases the payload of the VC, potentially significantly.
* s1_3_LinkToDegreeInRegistry.json: here the issuer uses their own identifier for the Credential but uses the `owl:sameAs` property to link to the same Credential in the registry.

### Sample VCs asserting Pat has achieved a Competency
CTDL can be used to describe Competencies. As with the BachelorDegree example above, more or less data about the competencies can be provided in the VC depending on the degree to which the parties are prepared to rely on being able to connect to get more data from the Registry, and how large a payload they can tolerate inside the VC.

* s_2_1_StandaloneCompetence.json: The competences are not stated to be associated with any other credential awarded.
* s_2_2_CompetenceAndDegree.json: the competencies Pat is stated to have acquired in the example above are the same as those required for the Bachelor's degree above, we can combine the two, illustrating a means of creating links between different achievements that may be useful in some scenarios.

### Sample VCs asserting Pat has completed a Course
Information about the courses Pat has completed may be useful

* s_3_1_CoursesFromDifferntOrgs.json: here the courses were taken at a different organization to that which awarded the credential.

### Sample VCs asserting Pat has passed an Assesment
It is of course important to know about the assessment of any competencies asserted.

* s_4_1_AssessmentAccreditation.json: The assessment is set by the issuer of the VC but accredited by another organization.
