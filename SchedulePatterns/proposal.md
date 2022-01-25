

### New Class
> **URI:** ceterms:ScheduledOffering
> **Label:** Scheduled Offering
> **Definition:** Offering of a Learning Opportunity or Assessment with a schedule associated with a specified location or modality.
> **Comment:** Provides data specific to a given scheduled offering; the Learning Opportunity (including Course and Learning Program) or Assessment being offered provides all data that is shared between different offerings.
> **Usage Note:** Rather than repeating data, only use this class if the data differs from the data provided by the resource for which this is an offer.

### New Properties

> **URI:** ceterms:hasOffering
> **Label:** Has Offering
> **Definition:** Indicates an offering and typical schedule.
> **Usage Note:** Only use this property when it is necessary and useful to provide data about specific offerings of a learning opportunity or assessment, such as particular combinations of schedule, location, and delivery.
> **Domain includes:** ceterms:LearningOpportunityProfile, ceterms:LearningProgram, ceterms:Course, ceterms:AssessmentProfile
> **Range includes:** ceterms:ScheduledOffering

> **URI:** ceterms:scheduleFrequencyType
> **Label:** schedule frequency type
> **Definition:**  Type of frequency indicating the cadence at which events associated with a resource typically occur; select from an existing enumeration of such types.
> **Comment:** This relates to the frequency of events associated with a resource once it is running
> **Usage Note:** For information relating to how often something starts, use ceterms:offerFrequencyType.
> **Domain includes:** ceterms:ScheduledOffering ceterms:LearningOpportunityProfile, ceterms:LearningProgram, ceterms:Course, ceterms:AssessmentProfile
> **Range includes:** ceterms:CredentialAlignmentObject
> **Target concept scheme:** https://purl.org/ctdl/vocabs/ScheduleFrequency

> **URI:** ceterms:scheduleTimingType
> **Label:** schedule timing type.
> **Definition:** Type of time at which events typically occur; select from an existing enumeration of such types.
> **Domain includes:** ceterms:ScheduledOffering ceterms:LearningOpportunityProfile, ceterms:LearningProgram, ceterms:Course, ceterms:AssessmentProfile
> **Range includes:** ceterms:CredentialAlignmentObject
> **Target concept scheme:** https://purl.org/ctdl/vocabs/ScheduleTiming


> **URI** ceterms:offerFrequencyType
> **Label:** offer frequency type.
> **Definition:** Type of frequency at which a resource is offered; select from an existing enumeration of such types.
> **Comment:** This relates to how often something starts.
> **Usage Note:** For information relating to the frequency of events associated with a resource when it is running, use ceterms:scheduleFrequencyType.
> **Domain includes:** ceterms:ScheduledOffering ceterms:LearningOpportunityProfile, ceterms:LearningProgram, ceterms:Course, ceterms:AssessmentProfile
> **Range includes:** ceterms:CredentialAlignmentObject
> **Target concept scheme:**  https://purl.org/ctdl/vocabs/ScheduleFrequency

### Modified Properties
**Add**
> **Subject:** ceterms:name
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

**Add**
> **Subject:** ceterms:description
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

**Add**
> **Subject:** ceterms:subjectWebpage
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

**Add**
> **Subject:** ceterms:availableAt
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

**Add**
> **Subject:** ceterms:offeredBy
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

**Add**
> **Subject:** ceterms:deliveryType
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

**Add**
> **Subject:** ceterms:deliveryTypeDescription
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

**Add**
> **Subject:** ceterms:estimatedCost
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

**Add**
> **Subject:** ceterms:commonCosts
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

**Add**
> **Subject:** ceterms:estimatedDuration
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

**Add**
> **Subject:** ceterms:aggregateData
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

**Add**
> **Subject:** ceterms:availableOnlineAt
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

**Add**
> **Subject:** ceterms:availabilityListing
> **Predicate:**  schema:domainIncludes
> **Object:** ceterms:ScheduledOffering

### New Concept Scheme (Frequency)

> **URI:** ceterms:ScheduleFrequency
> **Title:** Schedule Frequency
> **Description:** Types for how often events occur.

### New Concepts (Frequency)

> **URI:** scheduleFrequency:SingleInstance
> **Label:** Single Instance
> **Definition:** Schedule is a single instance of this event.
> **In Scheme:** ceterms:ScheduleFrequency

> **URI:** scheduleFrequency:Annually
> **Label:** Annually
> **Definition:** Schedule is on an annual basis, typically occurring once per year.
> **In Scheme:** ceterms:ScheduleFrequency

> **URI:** scheduleFrequency:SemiAnnually
> **Label:** Semi-annnually
> **Definition:** Schedule is on an semi-annual basis, typically occurring twice per year.
> **In Scheme:** ceterms:ScheduleFrequency

> **URI:** scheduleFrequency:Quarterly
> **Label:** Quarterly
> **Definition:** Schedule is on an quarterly basis, typically occurring three or four times per year.
> **In Scheme:** ceterms:ScheduleFrequency

> **URI:** scheduleFrequency:BiMonthly
> **Label:** BiMonthly
> **Definition:** Schedule is on a bi-monthly basis, typically occurring every other month.
> **In Scheme:** ceterms:ScheduleFrequency

> **URI:** scheduleFrequency:Monthly
> **Label:** Monthly
> **Definition:** Schedule is on a monthly basis, typically occurring once per month.
> **In Scheme:** ceterms:ScheduleFrequency

> **URI:** scheduleFrequency:Weekly
> **Label:** Weekly
> **Definition:** Schedule is on a weekly basis.
> **In Scheme:** ceterms:ScheduleFrequency

> **URI:** scheduleFrequency:MultiplePerWeek
> **Label:** Multiple Per Week
> **Definition:** Schedule is more frequent than a weekly basis.
> **In Scheme:** ceterms:ScheduleFrequency

> **URI:** scheduleFrequency:Irregular
> **Label:** Irregular
> **Definition:** Schedule has no significant pattern.
> **In Scheme:** ceterms:ScheduleFrequency

> **URI:** scheduleFrequency:SelfPaced
> **Label:** Self-Paced
> **Definition:** Schedule allows for participation at a self-determined pace, though some specific dates and deadlines may be involved.
> **In Scheme:** ceterms:ScheduleFrequency

> **URI:**  scheduleFrequency:OpenEntryExit
> **Label:** Open Entry/Exit
> **Definition:** Schedule allows for the participant to enter, participate with, and complete the resource during any period of the resource's run time.
> **In Scheme:** ceterms:ScheduleFrequency

> **URI:** scheduleFrequency:OnDemand
> **Label:** On-Demand
> **Definition:** Schedule allows the participant to choose when the resource begins.
> **In Scheme:** ceterms:ScheduleFrequency

> **URI:** scheduleFrequency:EventBased
> **Label:** Event Based
> **Definition:** Schedule is determined or triggered by other events, such as completion of activities, completion of the resource, events external to the resource, or other events; refer to the resource's information for details.
> **In Scheme:** ceterms:ScheduleFrequency

### New Concept Scheme (Timing)

> **URI:** ceterms:ScheduleTiming
> **Name:** Schedule Timing
> **Description:** Types for when in time events occur.

### New Concepts (Timing)

> **URI:** scheduleTiming:Daytime a skos:Concept
> **Label:** Daytime
> **Definition:** Schedule is typically during daytime hours.
> **In Scheme:** ceterms:ScheduleTiming

> **URI:** scheduleTiming:Evening  a skos:Concept
> **Label:** Evening
> **Definition:** Schedule is typically during evening hours.
> **In Scheme:** ceterms:ScheduleTiming

> **URI:** scheduleTiming:Weekdays a skos:Concept
> **Label:** Weekdays
> **Definition:** Schedule is Monday through Friday, consecutively or non-consecutively.
> **In Scheme:** ceterms:ScheduleTiming

> **URI:** scheduleTiming:Weekends a skos:Concept
> Label: Weekends
> Definition: Schedule is on a weekly basis, Saturday and/or Sunday.
> In Scheme: ceterms:ScheduleTiming
