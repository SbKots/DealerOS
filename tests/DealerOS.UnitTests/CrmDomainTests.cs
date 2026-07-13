using DealerOS.Modules.Crm.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.UnitTests;

public sealed class CrmDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Customer_NormalizesContactsAndRequiresConsentEvidence()
    {
        var customer = Customer.Create(Guid.NewGuid(), Guid.NewGuid(), CustomerType.Individual, " Иван Петров ",
            "8 (999) 123-45-67", " IVAN@EXAMPLE.COM ", PreferredContactChannel.Phone, true, true, Now,
            "web form", Guid.NewGuid(), Now);
        Assert.Equal("+79991234567", customer.NormalizedPhone);
        Assert.Equal("ivan@example.com", customer.NormalizedEmail);

        var error = Assert.Throws<DomainException>(() => Customer.Create(Guid.NewGuid(), Guid.NewGuid(),
            CustomerType.Individual, "Иван", "+79991234567", null, PreferredContactChannel.Phone, false, true,
            null, null, Guid.NewGuid(), Now));
        Assert.Equal("crm.consent_evidence_required", error.Code);
    }

    [Fact]
    public void CustomerMerge_IsExplicitAndIdempotentByCommandId()
    {
        var customer = CreateCustomer(); var commandId = Guid.NewGuid(); var target = Guid.NewGuid();
        Assert.True(customer.MergeInto(commandId, target, Guid.NewGuid(), "Дубликат телефона", 1, Now));
        Assert.False(customer.MergeInto(commandId, target, Guid.NewGuid(), "Дубликат телефона", 1, Now));
        Assert.True(customer.IsMerged);
        Assert.Equal(target, customer.MergedIntoCustomerId);
    }

    [Fact]
    public void FirstMeaningfulContact_IsIdempotentAndDoesNotRewriteSlaTimestamp()
    {
        var lead = CreateLead(); var manager = Guid.NewGuid(); var assignId = Guid.NewGuid();
        Assert.True(lead.Assign(assignId, manager, Guid.NewGuid(), 1, Now.AddMinutes(1)));
        Assert.False(lead.Assign(assignId, manager, Guid.NewGuid(), 1, Now.AddMinutes(2)));
        Assert.Equal(manager, lead.FindAssignedManagerForCommand(assignId));
        var contactId = Guid.NewGuid();
        Assert.True(lead.AddActivity(contactId, LeadActivityType.Call, LeadActivityDirection.Outbound, "Answered",
            "Обсудили автомобиль", true, "Назначить визит", Now.AddDays(1), manager, 2, Now.AddMinutes(5)));
        var firstResponse = lead.FirstResponseAt;
        Assert.False(lead.AddActivity(contactId, LeadActivityType.Call, LeadActivityDirection.Outbound, "Answered",
            "Обсудили автомобиль", true, "Назначить визит", Now.AddDays(1), manager, 4, Now.AddMinutes(10)));
        Assert.Equal(firstResponse, lead.FirstResponseAt);
        Assert.Equal(LeadStatus.FirstContact, lead.Status);
        Assert.False(lead.IsSlaBreached(Now.AddHours(2)));
    }

    [Fact]
    public void LeadRequiresFirstContactBeforeQualificationAndLostReason()
    {
        var lead = CreateLead();
        var error = Assert.Throws<DomainException>(() => lead.Qualify(Guid.NewGuid(), Guid.NewGuid(), "Визит",
            Now.AddDays(1), 1, Now));
        Assert.Equal("crm.first_contact_required", error.Code);
        Assert.Throws<DomainException>(() => lead.Close(Guid.NewGuid(), LeadStatus.Lost, Guid.NewGuid(), null, 1,
            Now));
        Assert.True(lead.IsSlaBreached(Now.AddMinutes(31)));
    }

    private static Customer CreateCustomer() => Customer.Create(Guid.NewGuid(), Guid.NewGuid(),
        CustomerType.Individual, "Иван", "+79991234567", null, PreferredContactChannel.Phone, false, false,
        null, null, Guid.NewGuid(), Now);
    private static Lead CreateLead() => Lead.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
        "Кроссовер до 3 млн", "Website", Guid.NewGuid(), 30, Now);
}
