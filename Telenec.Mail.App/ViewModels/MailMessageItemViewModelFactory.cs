using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.ViewModels;

public static class MailMessageItemViewModelFactory
{
    public static MailMessageItemViewModel Create(
        MailMessageData message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        return new MailMessageItemViewModel(
            sender:
                message.Sender,

            senderAddress:
                message.SenderAddress,

            recipientAddress:
                message.RecipientAddress,

            subject:
                message.Subject,

            preview:
                message.Preview,

            displayTime:
                message.DisplayTime,

            displayDateTime:
                message.DisplayDateTime,

            senderInitial:
                message.SenderInitial,

            greeting:
                message.Greeting,

            body:
                message.Body,

            closing:
                message.Closing,

            signature:
                message.Signature,

            isUnread:
                message.IsUnread,

            emphasizeSender:
                message.EmphasizeSender,

            highlightTitle:
                message.HighlightTitle,

            highlightText:
                message.HighlightText,

            htmlBody:
                message.HtmlBody,

            uniqueId:
                message.UniqueId,

            attachments:
                message.Attachments,

            hasSmimeSignature:
                message.HasSmimeSignature,

            messageId:
                message.MessageId,

            references:
                message.References,

            toAddresses:
                message.ToAddresses,

            ccAddresses:
                message.CcAddresses,

            replyToAddresses:
                message.ReplyToAddresses,

            importance:
                message.Importance,

            readReceipt:
                message.ReadReceipt,

            receivedReadReceipts:
                message.ReceivedReadReceipts,

            keywords:
                message.Keywords,

            securityData:
                message.SecurityData,

            smimeVerification:
                message.SmimeVerification);
    }
}